using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Exceptions;
using IBeam.Services.Abstractions;
namespace IBeam.Identity.Services.Invites;

public sealed partial class TenantInviteService : IExternalTenantInviteService, IManagedTenantInviteDeliveryService
{
    public bool SupportsDurableCreation => _invites is IExternalTenantInviteStore && _externalRoles is not null;
    public Task<TenantInviteInfo> EnsureAsync(ExternalTenantInviteRequest request, CancellationToken ct = default)
        => _operations.ExecuteAsync(this, async cancellation => {
            if (_invites is not IExternalTenantInviteStore store || _externalRoles is null || request is null || request.TenantId == Guid.Empty ||
                request.InviteId == Guid.Empty || request.Actor is null || request.Actor.OperationId == Guid.Empty ||
                request.Actor.OrganizationId == Guid.Empty || request.Actor.UserId == Guid.Empty || request.Actor.ApplicationId == Guid.Empty ||
                request.Token.Length < 43 || request.Token.Length > 128 || request.RoleIds.Count is < 1 or > 8 ||
                request.RoleIds.Any(x => x == Guid.Empty) || request.ApprovedRoleNames is not { Count: > 0 and <= 32 } ||
                request.ApprovedRoleNames.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100 || x.Any(char.IsControl) || x.Contains('*') || x.Contains('?')) || request.ExpiresUtc <= DateTimeOffset.UtcNow ||
                request.ExpiresUtc - request.CreatedUtc > TimeSpan.FromDays(7)) throw new IdentityValidationException("External invitation is invalid.");
            _ = await _tenants.FindByIdAsync(request.TenantId, cancellation) ?? throw new IdentityValidationException("Tenant not found.");
            var destination = NormalizeDestination(new(TenantInviteDestinationTypes.Email, Email: request.Email));
            // Roles must already exist locally (normally seeded by tenant lifecycle hooks). No role creation here.
            foreach (var id in request.RoleIds) {
                var role = await _externalRoles.GetRoleAsync(request.TenantId, id, cancellation);
                if (role is null || role.TenantId != request.TenantId || !role.IsActive || !request.ApprovedRoleNames.Contains(role.Name, StringComparer.Ordinal))
                    throw new IdentityValidationException("External invitation role is unavailable.");
            }
            var row = new TenantInviteRecord(request.InviteId, request.TenantId, destination.Item1, destination.Item2,
                HashToken(request.Token), TenantInviteStatuses.Pending, request.CreatedUtc, Guid.Empty, request.ExpiresUtc,
                RoleIds: request.RoleIds, RequirePasswordSetup: true, CausationId: request.Actor.OperationId.ToString("D"),
                Metadata: new Dictionary<string, string> { ["origin"] = "external-management",
                    ["support_organization_id"] = request.Actor.OrganizationId.ToString("D"),
                    ["support_user_id"] = request.Actor.UserId.ToString("D"),
                    ["management_application_id"] = request.Actor.ApplicationId.ToString("D") });
            return TenantInviteInfo.FromRecord(await store.EnsureAsync(row, cancellation));
        }, new ServiceOperationExecutionOptions { OperationName = "identity.tenantinvites.external.ensure",
            TenantId = request?.TenantId, EntityId = request?.InviteId, PermissionEnabled = false, AuditEnabled = false }, ct);

    public Task DeliverAsync(Guid tenantId, Guid inviteId, Guid operationId, string token, CancellationToken ct = default)
        => _operations.ExecuteAsync(this, async cancellation => {
            var invite = await RequireInviteAsync(tenantId, inviteId, cancellation);
            if (operationId == Guid.Empty || invite.InvitedByUserId != Guid.Empty || invite.CausationId != operationId.ToString("D") ||
                invite.TokenHash != HashToken(token) || !invite.IsRedeemable(DateTimeOffset.UtcNow))
                throw new IdentityValidationException("External invitation cannot be delivered.");
            // Reuse the application's approved link and message policies. Do not rewrite the invite after SMTP:
            // acceptance may have concurrently redeemed it. Durable delivery state belongs to the caller's journal.
            await DeliverMessage(invite, token, cancellation);
        }, new ServiceOperationExecutionOptions { OperationName = "identity.tenantinvites.external.deliver",
            TenantId = tenantId, EntityId = inviteId, PermissionEnabled = false, AuditEnabled = false }, ct);

    Task IManagedTenantInviteDeliveryService.DeliverAsync(Guid tenantId, Guid inviteId, Guid operationId, string token, CancellationToken ct)
        => _operations.ExecuteAsync(this, async cancellation => {
            var invite = await RequireInviteAsync(tenantId, inviteId, cancellation);
            if (_invites is not IManagedTenantInviteStore || invite.LastTransitionOperationId != operationId ||
                invite.TokenHash != HashToken(token) || !invite.IsRedeemable(DateTimeOffset.UtcNow) || invite.AcceptanceUserId is not null)
                throw new IdentityConcurrencyException();
            await DeliverMessage(invite, token, cancellation);
        }, new ServiceOperationExecutionOptions { OperationName = "identity.tenantinvites.external.redeliver", TenantId = tenantId, EntityId = inviteId,
            PermissionEnabled = false, AuditEnabled = false }, ct);
    private async Task DeliverMessage(TenantInviteRecord invite, string token, CancellationToken ct)
    {
        IdentitySenderMessage message;
        try { message = _messageFactory.CreateMessage(invite, token, _urlBuilder.BuildInviteUrl(invite, token)); }
        catch { throw new IdentityInviteDeliveryNotSentException(); }
        try { await _sender.SendAsync(message, ct); }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException("External invitation delivery is unconfirmed."); }
    }
}
