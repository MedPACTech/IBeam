using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantInviteService
{
    Task<TenantInviteCreatedResult> CreateInviteAsync(
        Guid tenantId,
        TenantInviteCreateRequest request,
        Guid invitedByUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<TenantInviteInfo>> ListInvitesAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantInviteInfo?> GetInviteAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default);
    Task<TenantInviteCreatedResult> ResendInviteAsync(Guid tenantId, Guid inviteId, Guid resentByUserId, CancellationToken ct = default);
    Task<TenantInviteInfo> RevokeInviteAsync(Guid tenantId, Guid inviteId, Guid revokedByUserId, string? reason = null, CancellationToken ct = default);
    Task<TenantInvitePreview> PreviewInviteAsync(string tokenOrCode, CancellationToken ct = default);

    Task<TenantInviteAcceptResult> AcceptInviteAsync(
        TenantInviteAcceptRequest request,
        Guid? authenticatedUserId = null,
        CancellationToken ct = default);
}
