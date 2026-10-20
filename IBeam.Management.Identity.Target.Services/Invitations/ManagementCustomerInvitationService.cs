using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Invitations;

public sealed class ManagementCustomerInvitationService(IManagementCustomerInvitationTransitionRepository repository,
    IManagementIdentityOperationService receipts, ITenantInviteStore invites, IManagedTenantInviteStore transitions,
    IManagedTenantInviteDeliveryService delivery, IManagementCustomerInvitationReadRepository reads,
    IManagementAssignableRoleReadRepository roles, IIdentityTenantStore tenants,
    IManagementCustomerIdentityTargetAccessService access, IOptions<ManagementCustomerInvitationOptions> policy,
    IOptions<ManagementIdentityOperationOptions> options, TimeProvider clock, IServiceOperationExecutor operations) : IManagementCustomerInvitationService
{
    private const string Authority = ManagementCustomerIdentityOperations.InvitesManage;
    public Task<ManagementCustomerInvitationResult> ResendAsync(Guid tenantId, Guid inviteId, string key, string version, CancellationToken ct = default)
        => Change(tenantId, inviteId, "resend", key, version, ct);
    public Task<ManagementCustomerInvitationResult> RevokeAsync(Guid tenantId, Guid inviteId, string key, string version, CancellationToken ct = default)
        => Change(tenantId, inviteId, "revoke", key, version, ct);
    private Task<ManagementCustomerInvitationResult> Change(Guid tenant, Guid id, string kind, string key, string version, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            var cancellation = deadline.Token;
            try {
                var actor = await access.RequireAsync(Authority, cancellation);
                if (!policy.Value.IsValid() || !options.Value.IsValid() || tenant == Guid.Empty || id == Guid.Empty ||
                    string.IsNullOrWhiteSpace(version) || version == "*" || version.Length > 256 || version.Any(char.IsControl)) throw new ManagementCustomerIdentityInvalidRequestException();
                var scope = new ManagementTenantAdministrationScope(actor.IdentityRealmId, actor.ApplicationKey, actor.Environment);
                await RequireTenant(tenant, kind == "resend", cancellation);
                var receipt = await receipts.ExecuteAsync(new() { Operation = Authority, ResourceScope = tenant, ExistingResourceId = id,
                    IdempotencyKey = key, CanonicalPayload = JsonSerializer.SerializeToUtf8Bytes(new { Kind = kind, ExpectedVersion = version }) },
                    new Handler(async (context, cancel) => {
                        var row = await Read(scope, tenant, context.OperationId, id, kind, cancel);
                        var current = await Native(tenant, id, cancel);
                        if (current.LastTransitionOperationId == context.OperationId) {
                            if (row is null) return ManagementIdentityReconciliation.Unknown;
                            var saved = Unprotect(row);
                            await transitions.TransitionAsync(Transition(row, saved), cancel); // Repairs a missing cross-partition token index only.
                            return kind == "revoke" || row.DeliveryStatus != "pending" ? ManagementIdentityReconciliation.Applied : ManagementIdentityReconciliation.Absent;
                        }
                        if (row?.Stage == "applied") return ManagementIdentityReconciliation.Applied; // A later transition must never be undone by recovery.
                        Precondition(current, version);
                        return ManagementIdentityReconciliation.Absent;
                    }, async (context, cancel) => {
                        var row = await Read(scope, tenant, context.OperationId, id, kind, cancel);
                        var current = await Native(tenant, id, cancel);
                        if (row is null) {
                            Precondition(current, version);
                            var now = clock.GetUtcNow();
                            if (kind == "resend") await ResendAllowed(current, now, cancel);
                            var window = current.ResendWindowUtc is DateTimeOffset start && now - start < TimeSpan.FromDays(1) ? start : now;
                            var count = current.ResendWindowUtc == window ? current.ResendCount + 1 : 1;
                            row = new() { Scope = scope, TenantId = tenant, InviteId = id, OperationId = context.OperationId,
                                OrganizationId = actor.OrganizationId, ApplicationId = actor.ApplicationId, UserId = actor.UserId, Kind = kind,
                                CreatedUtc = now, ExpiresUtc = now.AddDays(7) };
                            row = row with { EncryptedInput = Protect(row, new(version, kind == "resend" ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : "", window, count)) };
                            await repository.TryCreateAsync(row, cancel);
                            row = await Read(scope, tenant, context.OperationId, id, kind, cancel) ?? throw new ManagementCustomerIdentityUnavailableException();
                        }
                        var input = Unprotect(row);
                        if (row.Stage != "applied") {
                            await RequireTenant(tenant, kind == "resend", cancel);
                            if (current.LastTransitionOperationId != row.OperationId) {
                                Precondition(current, input.ExpectedVersion);
                                if (kind == "resend") await ResendAllowed(current, row.CreatedUtc, cancel);
                            }
                            await access.RequireAsync(Authority, cancel);
                            try { current = await transitions.TransitionAsync(Transition(row, input), cancel); }
                            catch (IdentityConcurrencyException) { throw new ManagementCustomerIdentityPreconditionException(); }
                            row = await repository.ReplaceAsync(row with { Stage = "applied", DeliveryStatus = kind == "revoke" ? "not-required" : "pending" }, row.Version, cancel);
                        }
                        if (kind == "revoke" || row.DeliveryStatus != "pending") return;
                        current = await Native(tenant, id, cancel);
                        if (current.LastTransitionOperationId != row.OperationId || current.AcceptanceUserId is not null || !current.IsRedeemable(clock.GetUtcNow())) {
                            await repository.ReplaceAsync(row with { DeliveryStatus = "superseded" }, row.Version, cancel); return;
                        }
                        await RequireTenant(tenant, true, cancel);
                        row = await repository.ReplaceAsync(row with { DeliveryStatus = "sending", DeliveryLeaseUtc = clock.GetUtcNow().AddMinutes(2) }, row.Version, cancel);
                        var status = "sent";
                        try { await access.RequireAsync(Authority, cancel); await delivery.DeliverAsync(tenant, id, row.OperationId, input.Token, cancel); }
                        catch (OperationCanceledException) { throw; }
                        catch (ManagementAccessDeniedException) { throw; }
                        catch (IdentityInviteDeliveryNotSentException) { status = "failed"; }
                        catch { status = "unknown"; }
                        await repository.ReplaceAsync(row with { DeliveryStatus = status, DeliveryLeaseUtc = null }, row.Version, cancel);
                    }), cancellation);
                var journal = await Read(scope, tenant, receipt.OperationId, id, kind, cancellation);
                var native = await Native(tenant, id, cancellation);
                var projected = await reads.FindAsync(tenant, id, cancellation) ?? throw new ManagementCustomerIdentityUnavailableException();
                if (projected.TenantId != tenant || projected.InviteId != id || projected.Version != native.Version) throw new ManagementCustomerIdentityUnavailableException();
                var state = native.Status.ToLowerInvariant();
                if (state is not ("pending" or "sent" or "expired" or "redeemed" or "revoked")) throw new ManagementCustomerIdentityUnavailableException();
                if (state is "pending" or "sent" && native.ExpiresUtc <= clock.GetUtcNow()) state = "expired";
                var status = state is "redeemed" or "revoked" or "expired" ? state : journal?.DeliveryStatus ?? "pending";
                if (status == "sending" && journal?.DeliveryLeaseUtc <= clock.GetUtcNow()) status = "unknown";
                var dto = new ManagementCustomerInvitationDto(tenant, id, native.DestinationType == "email" ? Users.ManagementCustomerDirectoryRuntime.Email(native.NormalizedDestination) : null,
                    null, null, state, Users.ManagementCustomerDirectoryRuntime.Roles(projected.RoleIdsCsv),
                    Users.ManagementCustomerDirectoryRuntime.RoleNames(projected.RoleNamesJson, true), projected.CreatedUtc, projected.ExpiresUtc,
                    projected.ModifiedUtc ?? projected.RevokedUtc ?? projected.RedeemedUtc ?? projected.SentUtc, projected.Version);
                await access.RequireAsync(Authority, cancellation);
                return new ManagementCustomerInvitationResult(receipt, status, dto);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or ManagementCustomerIdentityNotFoundException or
                ManagementIdentityOperationConflictException or ManagementCustomerIdentityPreconditionException or ManagementCustomerIdentityRateLimitException or ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = Authority + "." + kind, AuditAction = Authority + "." + kind,
            EntityId = id, EntityName = "ManagementCustomerInvitation", PermissionEnabled = false, AuditEnabled = true }, ct);

    private async Task<TenantInviteRecord> Native(Guid tenant, Guid id, CancellationToken ct)
    { var row = await invites.GetAsync(tenant, id, ct) ?? throw new ManagementCustomerIdentityNotFoundException();
      if (row.TenantId != tenant || row.InviteId != id) throw new ManagementCustomerIdentityNotFoundException(); return row; }
    private async Task RequireTenant(Guid id, bool active, CancellationToken ct)
    { var row = await tenants.FindByIdAsync(id, ct) ?? throw new ManagementCustomerIdentityNotFoundException();
      if (row.TenantId != id) throw new ManagementCustomerIdentityNotFoundException(); if (active && !row.IsActive) throw new ManagementIdentityOperationConflictException(); }
    private static void Precondition(TenantInviteRecord row, string version)
    { if (row.Version != version) throw new ManagementCustomerIdentityPreconditionException();
      if (row.AcceptanceUserId is not null || row.Status is TenantInviteStatuses.Redeemed or TenantInviteStatuses.Revoked) throw new ManagementIdentityOperationConflictException(); }
    private async Task ResendAllowed(TenantInviteRecord row, DateTimeOffset now, CancellationToken ct)
    {
        if (row.DestinationType != "email" || row.AccessGrants is { Count: > 0 } || row.SetAsDefaultTenant || row.RoleNames is { Count: > 0 } || row.RoleIds is not { Count: > 0 and <= 8 })
            throw new ManagementIdentityOperationConflictException();
        if (row.LastResendUtc is DateTimeOffset last && now - last < TimeSpan.FromSeconds(policy.Value.MinimumResendSeconds) ||
            row.ResendWindowUtc is DateTimeOffset window && now - window < TimeSpan.FromDays(1) && row.ResendCount >= policy.Value.MaximumResendsPerDay)
            throw new ManagementCustomerIdentityRateLimitException();
        var allowed = await roles.ReadAsync(row.TenantId, policy.Value.ApprovedRoleNames, ct);
        if (allowed.Count > 32 || row.RoleIds.Any(id => !allowed.Any(r => r.TenantId == row.TenantId && r.RoleId == id && r.IsActive && policy.Value.ApprovedRoleNames.Contains(r.Name, StringComparer.Ordinal))))
            throw new ManagementIdentityOperationConflictException();
    }
    private static ManagedTenantInviteTransition Transition(ManagementCustomerInvitationTransitionEntity row, Input input) => new() {
        TenantId = row.TenantId, InviteId = row.InviteId, OperationId = row.OperationId, Kind = row.Kind, ExpectedVersion = input.ExpectedVersion,
        TokenHash = row.Kind == "resend" ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Token))) : null,
        NowUtc = row.CreatedUtc, ExpiresUtc = row.Kind == "resend" ? row.ExpiresUtc : null, ResendWindowUtc = input.WindowUtc, ResendCount = input.Count,
        ExternalActor = new(row.OrganizationId, row.UserId, row.ApplicationId, row.OperationId) };
    private async Task<ManagementCustomerInvitationTransitionEntity?> Read(ManagementTenantAdministrationScope scope, Guid tenant, Guid operation, Guid invite, string kind, CancellationToken ct)
    { var row = await repository.GetAsync(scope, tenant, operation, ct);
      if (row is not null && (row.Scope != scope || row.TenantId != tenant || row.OperationId != operation || row.InviteId != invite || row.Kind != kind ||
          row.ExpiresUtc <= clock.GetUtcNow() || row.Stage is not ("reserved" or "applied") ||
          row.DeliveryStatus is not ("pending" or "sending" or "sent" or "failed" or "unknown" or "not-required" or "superseded"))) throw new ManagementIdentityOperationConflictException(); return row; }
    private byte[] Key() => HMACSHA256.HashData(Convert.FromBase64String(options.Value.FingerprintKey), Encoding.UTF8.GetBytes("ibeam-invitation-transition-recovery-v1"));
    private static byte[] Binding(ManagementCustomerInvitationTransitionEntity row) => JsonSerializer.SerializeToUtf8Bytes(new {
        row.Scope, row.TenantId, row.InviteId, row.OperationId, row.OrganizationId, row.ApplicationId, row.UserId, row.Kind, row.CreatedUtc, row.ExpiresUtc });
    private string Protect(ManagementCustomerInvitationTransitionEntity row, Input input)
    { var plain = JsonSerializer.SerializeToUtf8Bytes(input); var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var bytes = new byte[plain.Length];
      using var aes = new AesGcm(Key(), 16); aes.Encrypt(nonce, plain, bytes, tag, Binding(row)); return Convert.ToBase64String(nonce.Concat(tag).Concat(bytes).ToArray()); }
    private Input Unprotect(ManagementCustomerInvitationTransitionEntity row)
    { var bytes = Convert.FromBase64String(row.EncryptedInput); if (bytes.Length is < 29 or > 2048) throw new ManagementCustomerIdentityUnavailableException();
      var plain = new byte[bytes.Length - 28]; using var aes = new AesGcm(Key(), 16); aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Binding(row));
      return JsonSerializer.Deserialize<Input>(plain) ?? throw new ManagementCustomerIdentityUnavailableException(); }
    private sealed record Input(string ExpectedVersion, string Token, DateTimeOffset WindowUtc, int Count)
    { public override string ToString() => "Invitation recovery input (redacted)"; }
    private sealed class Handler(Func<ManagementIdentityMutationContext, CancellationToken, Task<ManagementIdentityReconciliation>> reconcile,
        Func<ManagementIdentityMutationContext, CancellationToken, Task> apply) : IManagementIdentityMutationHandler
    { public Task<ManagementIdentityReconciliation> ReconcileAsync(ManagementIdentityMutationContext context, CancellationToken ct) => reconcile(context, ct);
      public Task ApplyAsync(ManagementIdentityMutationContext context, CancellationToken ct) => apply(context, ct); }
}
