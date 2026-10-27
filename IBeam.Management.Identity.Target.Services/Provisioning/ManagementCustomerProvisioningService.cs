using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Provisioning;

public sealed class ManagementCustomerProvisioningService(IManagementCustomerProvisioningRepository repository,
    IManagementIdentityOperationService receipts, IManagementAssignableRoleCatalogService catalog,
    IExternalTenantInviteService externalInvites, ITenantInviteStore invites, IManagementCustomerInvitationReadRepository reads,
    IIdentityUserStore users, ITenantMembershipStore memberships, IIdentityTenantStore tenants,
    IManagementCustomerIdentityTargetAccessService access, IOptions<ManagementIdentityOperationOptions> options,
    TimeProvider clock, IServiceOperationExecutor operations) : IManagementCustomerProvisioningService
{
    public Task<ManagementCustomerProvisioningResult> ProvisionAsync(Guid tenantId, ProvisionManagementCustomerUser request, string key, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token => {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try {
                var actor = await access.RequireAsync(ManagementCustomerIdentityOperations.UsersProvision, deadline.Token);
                if (!options.Value.IsValid() || !externalInvites.SupportsDurableCreation || request is null || request.Email is null ||
                    request.Email.Length is < 3 or > 320 || request.Email.Any(char.IsControl) || !MailAddress.TryCreate(request.Email.Trim(), out var address) ||
                    address.Address != request.Email.Trim()) throw new ManagementCustomerIdentityInvalidRequestException();
                var approved = await catalog.RequireAsync(tenantId, request.PolicyKey, request.RoleIds, actor.Operation, deadline.Token);
                var input = new Intent(address.Address.ToLowerInvariant(), request.PolicyKey, approved.Select(x => x.RoleId).Order().ToArray(),
                    approved.Select(x => x.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), "");
                var scope = Scope(actor); var id = StableId(scope, tenantId, input); var fingerprint = Hash("fingerprint", JsonSerializer.Serialize(input));
                var existing = await Read(scope, tenantId, id, deadline.Token);
                if (existing is null) await Eligible(tenantId, input.Email, deadline.Token);
                else Check(existing, fingerprint);
                var receipt = await receipts.ExecuteAsync(new() { Operation = actor.Operation, ResourceScope = tenantId, ExistingResourceId = tenantId,
                    IdempotencyKey = key, CanonicalPayload = JsonSerializer.SerializeToUtf8Bytes(input) }, new Handler(async (context, cancellation) => {
                    var row = await Read(scope, tenantId, id, cancellation);
                    if (row is null) return ManagementIdentityReconciliation.Absent;
                    Check(row, fingerprint);
                    await repository.LinkOperationAsync(scope, tenantId, context.OperationId, id, cancellation);
                    return row.Stage == "invitation-persisted" && row.DeliveryStatus != "pending" ? ManagementIdentityReconciliation.Applied : ManagementIdentityReconciliation.Absent;
                }, async (context, cancellation) => {
                    var row = await Read(scope, tenantId, id, cancellation);
                    if (row is null) {
                        var now = clock.GetUtcNow();
                        row = new() { Scope = scope, TenantId = tenantId, InviteId = id, OriginalOperationId = context.OperationId,
                            OrganizationId = actor.OrganizationId, ApplicationId = actor.ApplicationId, CreatedByUserId = actor.UserId,
                            CreatedUtc = now, ExpiresUtc = now.AddDays(7), Fingerprint = fingerprint };
                        row = row with { EncryptedInput = Protect(row, input with { Token = ManagementInviteToken.Create() }) };
                        await repository.TryCreateAsync(row, cancellation);
                        row = await Read(scope, tenantId, id, cancellation) ?? throw new ManagementCustomerIdentityUnavailableException();
                    }
                    Check(row, fingerprint);
                    var saved = Unprotect(row);
                    await repository.LinkOperationAsync(scope, tenantId, context.OperationId, id, cancellation);
                    if (row.Stage != "invitation-persisted") {
                        await Active(tenantId, cancellation);
                        if (await invites.GetAsync(tenantId, id, cancellation) is null) await Eligible(tenantId, saved.Email, cancellation);
                        await catalog.RequireAsync(tenantId, saved.PolicyKey, saved.RoleIds, actor.Operation, cancellation);
                        await access.RequireAsync(actor.Operation, cancellation);
                        await externalInvites.EnsureAsync(new() { TenantId = tenantId, InviteId = id,
                            Actor = new(row.OrganizationId, row.CreatedByUserId, row.ApplicationId, row.OriginalOperationId), Email = saved.Email,
                            Token = saved.Token, RoleIds = saved.RoleIds, ApprovedRoleNames = saved.RoleNames, CreatedUtc = row.CreatedUtc, ExpiresUtc = row.ExpiresUtc }, cancellation);
                        row = await repository.ReplaceAsync(row with { Stage = "invitation-persisted" }, row.Version, cancellation);
                    }
                    if (row.DeliveryStatus != "pending") return;
                    var invite = await invites.GetAsync(tenantId, id, cancellation) ?? throw new ManagementCustomerIdentityUnavailableException();
                    if (!invite.IsRedeemable(clock.GetUtcNow())) return;
                    await Active(tenantId, cancellation);
                    row = await repository.ReplaceAsync(row with { DeliveryStatus = "sending", DeliveryClaim = Guid.NewGuid(),
                        DeliveryLeaseUtc = clock.GetUtcNow().AddMinutes(2), DeliveryAttempts = row.DeliveryAttempts + 1 }, row.Version, cancellation);
                    var delivery = "sent";
                    try { await access.RequireAsync(actor.Operation, cancellation); await externalInvites.DeliverAsync(tenantId, id, row.OriginalOperationId, saved.Token, cancellation); }
                    catch (OperationCanceledException) { throw; }
                    catch (ManagementAccessDeniedException) { throw; }
                    catch (IdentityInviteDeliveryNotSentException) { delivery = "failed"; }
                    catch { delivery = "unknown"; }
                    await repository.ReplaceAsync(row with { DeliveryStatus = delivery, DeliveryLeaseUtc = null, DeliveryClaim = null }, row.Version, cancellation);
                }), deadline.Token);
                var current = await Read(scope, tenantId, id, deadline.Token);
                ManagementCustomerInvitationDto? dto = null; var status = "pending";
                if (current?.Stage == "invitation-persisted") {
                    var saved = Unprotect(current); var row = await reads.FindAsync(tenantId, id, deadline.Token) ?? throw new ManagementCustomerIdentityUnavailableException();
                    if (row.TenantId != tenantId || row.InviteId != id || row.DestinationType != "email" || !string.Equals(row.Destination, saved.Email, StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(row.Version) || row.Version.Length > 256) throw new ManagementCustomerIdentityUnavailableException();
                    var state = row.Status?.ToLowerInvariant();
                    if (state is not ("pending" or "sent" or "redeemed" or "expired" or "revoked")) throw new ManagementCustomerIdentityUnavailableException();
                    if (row.ExpiresUtc <= clock.GetUtcNow() && state is "pending" or "sent") state = "expired";
                    status = state is "redeemed" ? "accepted" : state is "revoked" or "expired" ? state :
                        current.DeliveryStatus == "sending" && current.DeliveryLeaseUtc <= clock.GetUtcNow() ? "unknown" : current.DeliveryStatus;
                    dto = new(tenantId, id, saved.Email, null, null, state, saved.RoleIds, saved.RoleNames, row.CreatedUtc, row.ExpiresUtc,
                        row.ModifiedUtc ?? row.SentUtc ?? row.RedeemedUtc ?? row.RevokedUtc, row.Version);
                    var deliveryStatus = current.DeliveryStatus == "sending" && current.DeliveryLeaseUtc <= clock.GetUtcNow() ? "unknown" : current.DeliveryStatus;
                    receipt = receipt with { Invitation = new(tenantId, id, state == "redeemed" ? "accepted" : state, deliveryStatus, row.Version) };
                }
                await access.RequireAsync(actor.Operation, deadline.Token);
                return new ManagementCustomerProvisioningResult(receipt, status, dto);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or ManagementCustomerIdentityNotFoundException or
                ManagementIdentityOperationConflictException or ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = ManagementCustomerIdentityOperations.UsersProvision, AuditAction = ManagementCustomerIdentityOperations.UsersProvision,
            EntityId = tenantId, EntityName = "ManagementCustomerProvisioning", PermissionEnabled = false, AuditEnabled = true }, ct);

    private async Task Active(Guid tenantId, CancellationToken ct)
    { var tenant = await tenants.FindByIdAsync(tenantId, ct) ?? throw new ManagementCustomerIdentityNotFoundException(); if (tenant.TenantId != tenantId || !tenant.IsActive) throw new ManagementIdentityOperationConflictException(); }
    private async Task Eligible(Guid tenantId, string email, CancellationToken ct)
    {
        await Active(tenantId, ct);
        var user = await users.FindByEmailAsync(email, ct);
        if (user is not null && await memberships.GetTenantForUserAsync(user.UserId, tenantId, ct) is not null) throw new ManagementIdentityOperationConflictException();
    }
    private async Task<ManagementCustomerProvisioningEntity?> Read(ManagementTenantAdministrationScope scope, Guid tenant, Guid id, CancellationToken ct)
    {
        var row = await repository.GetAsync(scope, tenant, id, ct);
        if (row is not null && (row.Scope != scope || row.TenantId != tenant || row.InviteId != id || row.OriginalOperationId == Guid.Empty ||
            row.Stage is not ("reserved" or "invitation-persisted") || row.DeliveryStatus is not ("pending" or "sending" or "sent" or "failed" or "unknown") ||
            row.ExpiresUtc <= clock.GetUtcNow())) throw new ManagementIdentityOperationConflictException();
        return row;
    }
    private static void Check(ManagementCustomerProvisioningEntity row, string fingerprint)
    { if (row.Fingerprint != fingerprint) throw new ManagementIdentityOperationConflictException(); }
    private static ManagementTenantAdministrationScope Scope(ManagementDelegatedActor actor) => new(actor.IdentityRealmId, actor.ApplicationKey, actor.Environment);
    private byte[] Key(string purpose) => HMACSHA256.HashData(Convert.FromBase64String(options.Value.FingerprintKey), Encoding.UTF8.GetBytes("ibeam-customer-provisioning-v1:" + purpose));
    private string Hash(string purpose, string value) => Convert.ToHexString(HMACSHA256.HashData(Key(purpose), Encoding.UTF8.GetBytes(value)));
    private Guid StableId(ManagementTenantAdministrationScope scope, Guid tenant, Intent input) => new(Convert.FromHexString(Hash("invite-identity", JsonSerializer.Serialize(new { scope, tenant, input.Email, input.PolicyKey }))).AsSpan(0, 16));
    private static byte[] Binding(ManagementCustomerProvisioningEntity row) => JsonSerializer.SerializeToUtf8Bytes(new { row.Scope, row.TenantId, row.InviteId,
        row.OriginalOperationId, row.OrganizationId, row.ApplicationId, row.CreatedByUserId, row.CreatedUtc, row.ExpiresUtc });
    private string Protect(ManagementCustomerProvisioningEntity row, Intent input)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(input); var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var encrypted = new byte[plain.Length];
        using var aes = new AesGcm(Key("recovery"), 16); aes.Encrypt(nonce, plain, encrypted, tag, Binding(row));
        return Convert.ToBase64String(nonce.Concat(tag).Concat(encrypted).ToArray());
    }
    private Intent Unprotect(ManagementCustomerProvisioningEntity row)
    {
        var bytes = Convert.FromBase64String(row.EncryptedInput); if (bytes.Length is < 29 or > 8192) throw new ManagementCustomerIdentityUnavailableException();
        var plain = new byte[bytes.Length - 28]; using var aes = new AesGcm(Key("recovery"), 16); aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Binding(row));
        var value = JsonSerializer.Deserialize<Intent>(plain) ?? throw new ManagementCustomerIdentityUnavailableException();
        if (!ManagementInviteToken.IsRecoveryToken(value.Token) || value.RoleIds.Length is < 1 or > 8 || value.RoleNames.Any(x => !ManagementCustomerProvisioningOptions.ValidRoleName(x))) throw new ManagementCustomerIdentityUnavailableException();
        return value;
    }
    private sealed record Intent(string Email, string PolicyKey, Guid[] RoleIds, string[] RoleNames, string Token)
    { public override string ToString() => "Provisioning recovery input (redacted)"; }
    private sealed class Handler(Func<ManagementIdentityMutationContext, CancellationToken, Task<ManagementIdentityReconciliation>> reconcile,
        Func<ManagementIdentityMutationContext, CancellationToken, Task> apply) : IManagementIdentityMutationHandler
    {
        public Task<ManagementIdentityReconciliation> ReconcileAsync(ManagementIdentityMutationContext context, CancellationToken ct) => reconcile(context, ct);
        public Task ApplyAsync(ManagementIdentityMutationContext context, CancellationToken ct) => apply(context, ct);
    }
}
