using System.Security.Claims;

namespace IBeam.AccessControl.Services;

public sealed class PermissionRoleAuthorizer : IPermissionRoleAuthorizer
{
    private readonly IPermissionRoleMapStore _store;

    public PermissionRoleAuthorizer(IPermissionRoleMapStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<bool> AuthorizeAsync(
        Guid tenantId,
        ClaimsPrincipal principal,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default)
    {
        PermissionRoleMapService.ValidateTenantId(tenantId);

        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        var grants = await _store.ResolveGrantsAsync(
            tenantId,
            permissionNames ?? Array.Empty<string>(),
            permissionIds ?? Array.Empty<Guid>(),
            ct).ConfigureAwait(false);

        if (!grants.HasAnyGrant)
            return false;

        var roleNames = GetRoleNames(principal);
        if (grants.RoleNames.Any(roleNames.Contains))
            return true;

        var roleIds = GetRoleIds(principal);
        return grants.RoleIds.Any(roleIds.Contains);
    }

    private static HashSet<string> GetRoleNames(ClaimsPrincipal principal)
        => principal.Claims
            .Where(x =>
                string.Equals(x.Type, "role", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<Guid> GetRoleIds(ClaimsPrincipal principal)
        => principal.Claims
            .Where(x =>
                string.Equals(x.Type, "rid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, "role_id", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();
}
