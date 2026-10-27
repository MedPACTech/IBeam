using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Exceptions;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Onboarding;

public sealed class ManagementEnterpriseOnboardingService(IManagementEnterpriseOnboardingRepository repository,
    IManagementIdentityOperationService receipts, IManagementIdentityOperationLookupService lookup,
    IManagementTenantAdministrationService tenants, IExternalTenantInviteService externalInvites, ITenantInviteStore invites,
    ITenantRoleStore roles, IIdentityUserStore users, ITenantMembershipStore memberships,
    IManagementCustomerIdentityTargetAccessService access, IOptions<ManagementEnterpriseOnboardingOptions> policies,
    IOptions<ManagementIdentityOperationOptions> options, TimeProvider clock, IServiceOperationExecutor operations, IManagementCustomerOnboardingPolicy? policyProvider = null,
    IEnumerable<IManagementCustomerOnboardingInitializer>? initializers = null)
    : IManagementEnterpriseOnboardingService
{
    public Task<ManagementEnterpriseOnboardingDto> CreateAsync(CreateManagementEnterpriseOnboarding request, string idempotencyKey, CancellationToken ct = default)
        => Run(ManagementCustomerIdentityOperations.OnboardingCreate, async (actor, token) => {
            var input = await Validate(request, token);
            ManagementIdentityOperationDto receipt;
            try {
                receipt = await receipts.ExecuteAsync(new() { Operation = actor.Operation, IdempotencyKey = idempotencyKey,
                    CanonicalPayload = JsonSerializer.SerializeToUtf8Bytes(input with { Token = "" }) },
                    new Handler(async (context, cancellation) => {
                        var row = await Read(actor, context.ResourceId, cancellation);
                        return row?.Step == "invitation-persisted" && row.DeliveryStatus != "pending" ? ManagementIdentityReconciliation.Applied : ManagementIdentityReconciliation.Absent;
                    }, async (context, cancellation) => {
                        var row = await Read(actor, context.ResourceId, cancellation);
                        if (row is null) {
                            var now = clock.GetUtcNow();
                            row = new() { Scope = Scope(actor), ApplicationKey = actor.ApplicationKey, Environment = actor.Environment,
                                OperationId = context.OperationId, OnboardingId = context.ResourceId, InviteId = StableInvite(context.OperationId), CreatedByUserId = actor.UserId,
                                CreatedUtc = now, ExpiresUtc = now.AddDays(7) };
                            row = row with { EncryptedInput = Protect(row, input with { Token = ManagementInviteToken.Create() }) };
                            await repository.TryCreateAsync(row, cancellation);
                            row = await Read(actor, context.ResourceId, cancellation) ?? throw new ManagementCustomerIdentityUnavailableException();
                        }
                        await Resume(actor, row, false, cancellation);
                    }), token);
            } catch (ManagementCustomerIdentityUnavailableException) {
                receipt = await lookup.ResolveAsync(new(actor.Operation, Guid.Empty, null), idempotencyKey, actor.Operation, token);
            }
            var job = await Read(actor, receipt.ResourceId, token);
            return job is null ? new(receipt.OperationId, receipt.ResourceId, null, StableInvite(receipt.OperationId),
                "in-progress", "tenant-creation", "pending", false, null, receipt.CreatedUtc, receipt.ExpiresUtc) : await Project(actor, job, token);
        }, ct);

    public Task<ManagementEnterpriseOnboardingDto> GetAsync(Guid onboardingId, string authority, CancellationToken ct = default)
        => Run(authority, async (actor, token) => {
            if (authority is not (ManagementCustomerIdentityOperations.OnboardingRead or ManagementCustomerIdentityOperations.OnboardingCreate or ManagementCustomerIdentityOperations.OnboardingRetry))
                throw new ManagementAccessDeniedException();
            var row = await Read(actor, onboardingId, token) ?? throw new ManagementCustomerIdentityNotFoundException();
            return await Project(actor, row, token);
        }, ct);

    public Task<ManagementEnterpriseOnboardingDto> RetryAsync(Guid onboardingId, string idempotencyKey, CancellationToken ct = default)
        => Run(ManagementCustomerIdentityOperations.OnboardingRetry, async (actor, token) => {
            var row = await Read(actor, onboardingId, token) ?? throw new ManagementCustomerIdentityNotFoundException();
            _ = await Unprotect(row, ct: token); // Current policy must still approve the persisted intent.
            try {
                await receipts.ExecuteAsync(new() { Operation = actor.Operation, ResourceScope = onboardingId, ExistingResourceId = onboardingId,
                    IdempotencyKey = idempotencyKey, CanonicalPayload = JsonSerializer.SerializeToUtf8Bytes(new { OnboardingId = onboardingId }) },
                    new Handler((_, _) => Task.FromResult(ManagementIdentityReconciliation.Absent), async (_, cancellation) =>
                        await Resume(actor, await Read(actor, onboardingId, cancellation) ?? throw new ManagementCustomerIdentityNotFoundException(), true, cancellation)), token);
            } catch (ManagementCustomerIdentityUnavailableException) { /* Business journal carries the safe recoverable state. */ }
            return await Project(actor, await Read(actor, onboardingId, token) ?? throw new ManagementCustomerIdentityNotFoundException(), token);
        }, ct);

    private async Task Resume(ManagementDelegatedActor actor, ManagementEnterpriseOnboardingEntity row, bool explicitRetry, CancellationToken ct)
    {
        try { await ResumeCore(actor, row, explicitRetry, ct); }
        catch (Exception ex) when (ex is not (OperationCanceledException or ManagementAccessDeniedException or ManagementIdentityOperationConflictException)) {
            var current = await Read(actor, row.OnboardingId, ct);
            if (current is not null && current.ErrorCode is null) await Save(current with { ErrorCode = "setup-recovery-required" }, ct);
            throw new ManagementCustomerIdentityUnavailableException();
        }
    }
    private async Task ResumeCore(ManagementDelegatedActor actor, ManagementEnterpriseOnboardingEntity row, bool explicitRetry, CancellationToken ct)
    {
        var input = await Unprotect(row, ct: ct);
        if (row.Step != "invitation-persisted") {
            ManagementTenantMutationResult tenant;
            try { tenant = await tenants.CreateAsync(new() { Name = input.TenantName, ExternalReference = input.ExternalReference }, ChildKey(row), ct); }
            catch (ManagementCustomerIdentityUnavailableException) {
                // A customer row can exist while its extension hook is still pending. Recover its reserved ID without another write.
                try {
                    var pending = await lookup.ResolveAsync(new(ManagementCustomerIdentityOperations.TenantsCreate, Guid.Empty, null), ChildKey(row), ManagementCustomerIdentityOperations.TenantsCreate, ct);
                    await Save(row with { TenantId = pending.ResourceId, Step = "tenant-extensions", ErrorCode = "tenant-setup-incomplete" }, ct);
                } catch (ManagementCustomerIdentityNotFoundException) { }
                throw;
            }
            row = await Save(row with { TenantId = tenant.Operation.ResourceId, Step = tenant.Operation.Status == "succeeded" ? "extensions-completed" : "tenant-extensions",
                ErrorCode = tenant.Operation.Status == "succeeded" ? null : "tenant-setup-incomplete" }, ct);
            if (tenant.Operation.Status != "succeeded") throw new ManagementCustomerIdentityUnavailableException();
            if (tenant.Tenant?.Status != "Active") throw new ManagementIdentityOperationConflictException();
            var tenantId = row.TenantId ?? throw new ManagementCustomerIdentityUnavailableException();
            if (input.InitializerKey is { } initializerKey) {
                var hook = (initializers ?? []).SingleOrDefault(x => x.Key == initializerKey);
                row = await Save(row with { Step = "consumer-initialization", ErrorCode = "consumer-initialization-incomplete" }, ct);
                if (hook is null) throw new ManagementCustomerIdentityUnavailableException();
                var context = new ManagementCustomerInitializationContext(actor.IdentityRealmId, actor.OrganizationId, actor.ApplicationId,
                    row.OperationId, row.OnboardingId, tenantId, input.PolicyKey);
                await access.RequireAsync(actor.Operation, ct);
                var state = await hook.ReconcileAsync(context, ct);
                if (state == ManagementCustomerInitializationState.Absent) {
                    await hook.InitializeAsync(context, ct);
                    state = await hook.ReconcileAsync(context, ct);
                }
                if (state != ManagementCustomerInitializationState.Applied) throw new ManagementCustomerIdentityUnavailableException();
                row = await Save(row with { Step = "extensions-completed", ErrorCode = null }, ct);
            }
            var approved = (await roles.GetRolesAsync(tenantId, ct)).Where(x => x.TenantId == tenantId && x.IsActive && x.Name == input.RoleName).ToArray();
            if (approved.Length != 1) {
                await Save(row with { ErrorCode = "administrator-role-unavailable" }, ct);
                throw new ManagementCustomerIdentityUnavailableException();
            }
            await access.RequireAsync(actor.Operation, ct);
            await externalInvites.EnsureAsync(new() { TenantId = tenantId, InviteId = row.InviteId,
                ApprovedRoleNames = [input.RoleName],
                Actor = new(actor.OrganizationId, row.CreatedByUserId, actor.ApplicationId, row.OperationId), Email = input.InitialAdminEmail,
                Token = input.Token, RoleIds = [approved[0].RoleId], CreatedUtc = row.CreatedUtc, ExpiresUtc = row.ExpiresUtc }, ct);
            row = await Save(row with { Step = "invitation-persisted", ErrorCode = null }, ct);
        }
        var invite = await invites.GetAsync(row.TenantId!.Value, row.InviteId, ct)
            ?? throw new ManagementCustomerIdentityUnavailableException();
        if (invite.Status == "redeemed" || invite.Status is "revoked" or "expired" || invite.ExpiresUtc <= clock.GetUtcNow()) return;
        if (row.DeliveryStatus == "sent" || row.DeliveryLeaseUtc > clock.GetUtcNow() ||
            !explicitRetry && row.DeliveryStatus != "pending") return;
        if (row.DeliveryAttempts >= options.Value.MaximumAttempts) throw new ManagementIdentityOperationConflictException();
        // CAS claim before SMTP. A process dying afterwards is unknown; ordinary create retries never resend.
        row = await Save(row with { DeliveryStatus = "sending", DeliveryClaim = Guid.NewGuid(), DeliveryLeaseUtc = clock.GetUtcNow().AddMinutes(2),
            DeliveryAttempts = row.DeliveryAttempts + 1, ErrorCode = null }, ct);
        var delivery = "sent";
        try {
            await access.RequireAsync(actor.Operation, ct);
            await externalInvites.DeliverAsync(row.TenantId!.Value, row.InviteId, row.OperationId, input.Token, ct);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ManagementAccessDeniedException) { throw; }
        catch (IdentityInviteDeliveryNotSentException) { delivery = "failed"; }
        catch { delivery = "unknown"; } // Provider failures can occur after acceptance by the mail server.
        await Save(row with { DeliveryStatus = delivery, DeliveryLeaseUtc = null, DeliveryClaim = null,
            ErrorCode = delivery == "sent" ? null : delivery == "failed" ? "delivery-not-sent" : "delivery-unconfirmed" }, ct);
    }

    private async Task<ManagementEnterpriseOnboardingDto> Project(ManagementDelegatedActor actor, ManagementEnterpriseOnboardingEntity row, CancellationToken ct)
    {
        var delivery = row.DeliveryStatus == "sending" && row.DeliveryLeaseUtc <= clock.GetUtcNow() ? "unknown" : row.DeliveryStatus;
        var status = row.ErrorCode is not null || delivery == "unknown" ? "recoverable-failure" : "in-progress";
        string? error = row.ErrorCode; var completed = false;
        if (row.Step == "invitation-persisted" && row.TenantId is Guid tenant) {
            var invite = await invites.GetAsync(tenant, row.InviteId, ct) ?? throw new ManagementCustomerIdentityUnavailableException();
            var input = await Unprotect(row, false, ct);
            if (invite.Status == "redeemed" && invite.RedeemedByUserId is Guid userId) {
                var user = await users.FindByIdAsync(userId, ct);
                var membership = await memberships.GetTenantForUserAsync(userId, tenant, ct);
                completed = user is { EmailConfirmed: true } && string.Equals(user.Email?.Trim(), input.InitialAdminEmail, StringComparison.OrdinalIgnoreCase) &&
                    membership is { IsActive: true } && membership.TenantId == tenant &&
                    invite.RoleIds is { Count: > 0 } ids && ids.All(x => membership.RoleIds?.Contains(x) == true);
                status = completed ? "completed" : "awaiting-acceptance";
            } else if (invite.Status is "revoked" or "expired" || invite.ExpiresUtc <= clock.GetUtcNow()) {
                status = "recoverable-failure"; error = "invitation-unavailable";
            } else if (delivery == "sent") status = "awaiting-acceptance";
        }
        await access.RequireAsync(actor.Operation, ct);
        return new(row.OperationId, row.OnboardingId, row.TenantId, row.InviteId, status, row.Step, delivery, completed, error, row.CreatedUtc, row.ExpiresUtc);
    }

    private async Task<ManagementEnterpriseOnboardingEntity?> Read(ManagementDelegatedActor actor, Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        var row = await repository.GetAsync(Scope(actor), id, ct);
        if (row is not null && (row.Scope != Scope(actor) || row.ApplicationKey != actor.ApplicationKey || row.Environment != actor.Environment ||
            row.OnboardingId != id || row.OperationId == Guid.Empty || row.CreatedByUserId == Guid.Empty || row.InviteId != StableInvite(row.OperationId) ||
            row.Step is not ("tenant-creation" or "tenant-extensions" or "extensions-completed" or "consumer-initialization" or "invitation-persisted") ||
            row.DeliveryStatus is not ("pending" or "sending" or "sent" or "failed" or "unknown") ||
            row.DeliveryAttempts is < 0 or > 100 || row.ErrorCode is not (null or "tenant-setup-incomplete" or "administrator-role-unavailable" or
                "consumer-initialization-incomplete" or "setup-recovery-required" or "delivery-not-sent" or "delivery-unconfirmed") ||
            row.ExpiresUtc <= clock.GetUtcNow())) throw new ManagementCustomerIdentityNotFoundException();
        return row;
    }
    private Task<ManagementEnterpriseOnboardingEntity> Save(ManagementEnterpriseOnboardingEntity row, CancellationToken ct)
        => repository.ReplaceAsync(row, row.Version, ct);
    private async Task<RecoveryInput> Validate(CreateManagementEnterpriseOnboarding request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TenantName) || request.TenantName.Length > 512 || request.TenantName.Any(char.IsControl) ||
            request.InitialAdminEmail is null || request.InitialAdminEmail.Length is < 3 or > 320 || request.InitialAdminEmail.Any(char.IsControl) ||
            !MailAddress.TryCreate(request.InitialAdminEmail.Trim(), out var address) || address.Address != request.InitialAdminEmail.Trim() ||
            !ManagementEnterpriseOnboardingOptions.ValidKey(request.PolicyKey) || !policies.Value.IsValid() ||
            request.ExternalReference is { } reference && (reference.Length > 128 || reference.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':' or '/'))))
            throw new ManagementCustomerIdentityInvalidRequestException();
        var policy = OnboardingPolicyValidation.Validate(await PolicyProvider.GetEnabledAsync(ct)).SingleOrDefault(x => x.Key == request.PolicyKey)
            ?? throw new ManagementAccessDeniedException();
        return new(request.TenantName.Trim(), address.Address.ToLowerInvariant(), request.ExternalReference is "" ? null : request.ExternalReference, request.PolicyKey, policy.InitialAdministratorRole, "", policy.InitializerKey);
    }
    private IManagementCustomerOnboardingPolicy PolicyProvider => policyProvider ?? new ConfiguredManagementCustomerOnboardingPolicy(policies);
    private byte[] Key() => HMACSHA256.HashData(Convert.FromBase64String(options.Value.FingerprintKey), Encoding.UTF8.GetBytes("ibeam-onboarding-recovery-v1"));
    private static byte[] Binding(ManagementEnterpriseOnboardingEntity row) => JsonSerializer.SerializeToUtf8Bytes(new {
        row.Scope, row.ApplicationKey, row.Environment, row.OperationId, row.OnboardingId, row.InviteId, row.CreatedByUserId, row.CreatedUtc, row.ExpiresUtc });
    private string Protect(ManagementEnterpriseOnboardingEntity row, RecoveryInput input)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(input); var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[body.Length];
        using var aes = new AesGcm(Key(), 16); aes.Encrypt(nonce, body, cipher, tag, Binding(row));
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    private async Task<RecoveryInput> Unprotect(ManagementEnterpriseOnboardingEntity row, bool requireCurrentPolicy = true, CancellationToken ct = default)
    {
        var bytes = Convert.FromBase64String(row.EncryptedInput);
        if (bytes.Length is < 29 or > 8192) throw new ManagementCustomerIdentityUnavailableException();
        var plain = new byte[bytes.Length - 28]; using var aes = new AesGcm(Key(), 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Binding(row));
        var input = JsonSerializer.Deserialize<RecoveryInput>(plain) ?? throw new ManagementCustomerIdentityUnavailableException();
        if (!ManagementInviteToken.IsRecoveryToken(input.Token) || !Provisioning.ManagementCustomerProvisioningOptions.ValidRoleName(input.RoleName)) throw new ManagementAccessDeniedException();
        if (requireCurrentPolicy) {
            var approved = await Validate(new() { TenantName = input.TenantName, InitialAdminEmail = input.InitialAdminEmail, ExternalReference = input.ExternalReference, PolicyKey = input.PolicyKey }, ct);
            if (approved.RoleName != input.RoleName || approved.InitializerKey != input.InitializerKey) throw new ManagementAccessDeniedException();
        }
        return input;
    }
    private static Guid StableInvite(Guid operation) => new(SHA256.HashData(Encoding.UTF8.GetBytes("onboarding-invite-v1:" + operation.ToString("D"))).AsSpan(0, 16));
    private static string ChildKey(ManagementEnterpriseOnboardingEntity row) => "onboard-" + row.OperationId.ToString("N") + "-tenant";
    private static ManagementIdentityOperationScope Scope(ManagementDelegatedActor actor) => new(actor.IdentityRealmId, actor.OrganizationId, actor.ApplicationId);
    private sealed record RecoveryInput(string TenantName, string InitialAdminEmail, string? ExternalReference, string PolicyKey, string RoleName, string Token,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? InitializerKey = null)
    { public override string ToString() => "Onboarding recovery input (redacted)"; }
    private Task<ManagementEnterpriseOnboardingDto> Run(string operation, Func<ManagementDelegatedActor, CancellationToken, Task<ManagementEnterpriseOnboardingDto>> run, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try {
                if (!options.Value.IsValid() || !policies.Value.IsValid() || !externalInvites.SupportsDurableCreation || !lookup.SupportsLookup)
                    throw new ManagementCustomerIdentityUnavailableException();
                return await run(await access.RequireAsync(operation, timeout.Token), timeout.Token);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or ManagementIdentityOperationConflictException or
                ManagementCustomerIdentityNotFoundException or ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = operation, EntityName = "ManagementEnterpriseOnboarding",
            PermissionEnabled = false, AuditEnabled = true }, ct);
    private sealed class Handler(Func<ManagementIdentityMutationContext, CancellationToken, Task<ManagementIdentityReconciliation>> reconcile,
        Func<ManagementIdentityMutationContext, CancellationToken, Task> apply) : IManagementIdentityMutationHandler
    {
        public Task<ManagementIdentityReconciliation> ReconcileAsync(ManagementIdentityMutationContext context, CancellationToken ct) => reconcile(context, ct);
        public Task ApplyAsync(ManagementIdentityMutationContext context, CancellationToken ct) => apply(context, ct);
    }
}
