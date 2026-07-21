using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Services.Abstractions;

namespace IBeam.Identity.Services.Tenants;

[IBeamOperation("identity.tenantusers.directory")]
public sealed class TenantUserDirectoryService : ITenantUserDirectoryService
{
    private readonly ITenantMembershipStore _memberships;
    private readonly ITenantInviteService _invites;
    private readonly ITenantRoleService _roles;
    private readonly IServiceOperationExecutor _operations;

    public TenantUserDirectoryService(
        ITenantMembershipStore memberships,
        ITenantInviteService invites,
        ITenantRoleService roles,
        IServiceOperationExecutor? operations = null)
    {
        _memberships = memberships ?? throw new ArgumentNullException(nameof(memberships));
        _invites = invites ?? throw new ArgumentNullException(nameof(invites));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("identity.tenantusers.directory.list")]
    public Task<IReadOnlyList<TenantUserDirectoryItem>> ListAsync(
        Guid tenantId,
        TenantUserDirectoryRequest? request = null,
        CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => ListCoreAsync(tenantId, request ?? new TenantUserDirectoryRequest(), token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct);

    private async Task<IReadOnlyList<TenantUserDirectoryItem>> ListCoreAsync(
        Guid tenantId,
        TenantUserDirectoryRequest request,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");

        var items = new List<TenantUserDirectoryItem>();

        if (!request.PendingOnly)
        {
            var users = await _memberships.GetUsersForTenantAsync(tenantId, ct).ConfigureAwait(false);
            items.AddRange(users
                .Where(x => request.IncludeDisabled || x.IsActive)
                .Select(MapUser));
        }

        if (request.IncludePending || request.PendingOnly)
        {
            var pendingInvites = await _invites.ListInvitesAsync(
                    tenantId,
                    new TenantInviteListRequest(ActiveOnly: true),
                    ct)
                .ConfigureAwait(false);

            var roleNameById = await BuildRoleNameMapAsync(tenantId, pendingInvites, ct).ConfigureAwait(false);
            items.AddRange(pendingInvites.Select(x => MapInvite(x, roleNameById)));
        }

        return items
            .OrderBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.DisplayName ?? x.Email ?? x.PhoneNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.CreatedUtc)
            .ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> BuildRoleNameMapAsync(
        Guid tenantId,
        IReadOnlyList<TenantInviteInfo> invites,
        CancellationToken ct)
    {
        if (!invites.Any(x => x.RoleIds.Count > 0))
            return new Dictionary<Guid, string>();

        var roles = await _roles.GetRolesAsync(tenantId, ct).ConfigureAwait(false);
        return roles
            .Where(x => x.RoleId != Guid.Empty && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.RoleId)
            .ToDictionary(x => x.Key, x => x.First().Name);
    }

    private static TenantUserDirectoryItem MapUser(TenantUserInfo user)
        => new(
            Kind: TenantUserDirectoryItemKinds.User,
            TenantId: user.TenantId,
            UserId: user.UserId,
            Email: user.Email,
            PhoneNumber: user.PhoneNumber,
            DisplayName: user.DisplayName,
            Status: user.IsActive ? TenantUserDirectoryStatuses.Active : TenantUserDirectoryStatuses.Disabled,
            RoleIds: user.RoleIds ?? [],
            RoleNames: user.Roles,
            CreatedUtc: user.CreatedAt,
            UpdatedUtc: user.DisabledAt);

    private static TenantUserDirectoryItem MapInvite(
        TenantInviteInfo invite,
        IReadOnlyDictionary<Guid, string> roleNameById)
    {
        var email = string.Equals(invite.DestinationType, TenantInviteDestinationTypes.Email, StringComparison.OrdinalIgnoreCase)
            ? invite.NormalizedDestination
            : null;
        var phone = string.Equals(invite.DestinationType, TenantInviteDestinationTypes.Sms, StringComparison.OrdinalIgnoreCase)
            ? invite.NormalizedDestination
            : null;
        var roleNames = invite.RoleNames
            .Concat(invite.RoleIds.Select(x => roleNameById.TryGetValue(x, out var name) ? name : null)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TenantUserDirectoryItem(
            Kind: TenantUserDirectoryItemKinds.Invite,
            TenantId: invite.TenantId,
            InviteId: invite.InviteId,
            Email: email,
            PhoneNumber: phone,
            DisplayName: invite.ProfileHints?.DisplayName,
            FirstName: invite.ProfileHints?.FirstName,
            LastName: invite.ProfileHints?.LastName,
            Status: invite.Status,
            RoleIds: invite.RoleIds,
            RoleNames: roleNames,
            CreatedUtc: invite.CreatedUtc,
            UpdatedUtc: invite.SentUtc ?? invite.RevokedUtc ?? invite.RedeemedUtc,
            InvitedByUserId: invite.InvitedByUserId,
            ExpiresUtc: invite.ExpiresUtc,
            RedeemedByUserId: invite.RedeemedByUserId,
            RedeemedUtc: invite.RedeemedUtc);
    }
}
