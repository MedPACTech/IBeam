using IBeam.Identity.Options;

namespace IBeam.Identity.Services.Authorization;

public sealed class PermissionAccessMapBuilder
{
    private readonly List<PermissionAccessMapEntry> _entries = [];

    public PermissionAccessMapBuilder AddPermission(string permissionName, params string[] roleNames)
        => AllowRolesForPermission(permissionName, roleNames, tenantId: null);

    public PermissionAccessMapBuilder AddPermission(Guid permissionId, params string[] roleNames)
        => AllowForPermissionId(permissionId, roleNames: roleNames, roleIds: null, tenantId: null);

    public PermissionAccessMapBuilder AllowRolesForPermission(
        string permissionName,
        IEnumerable<string> roleNames,
        Guid? tenantId = null)
    {
        _entries.Add(new PermissionAccessMapEntry
        {
            TenantId = tenantId,
            PermissionName = permissionName,
            RoleNames = roleNames?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [],
            RoleIds = []
        });
        return this;
    }

    public PermissionAccessMapBuilder AllowRolesForPermission(
        string permissionName,
        Guid? tenantId = null,
        params string[] roleNames)
        => AllowRolesForPermission(permissionName, roleNames, tenantId);

    public PermissionAccessMapBuilder AllowRoleIdsForPermission(
        string permissionName,
        IEnumerable<Guid> roleIds,
        Guid? tenantId = null)
    {
        _entries.Add(new PermissionAccessMapEntry
        {
            TenantId = tenantId,
            PermissionName = permissionName,
            RoleNames = [],
            RoleIds = roleIds.Where(x => x != Guid.Empty).Distinct().ToList()
        });
        return this;
    }

    public PermissionAccessMapBuilder AllowForPermissionId(
        Guid permissionId,
        IEnumerable<string>? roleNames = null,
        IEnumerable<Guid>? roleIds = null,
        Guid? tenantId = null)
    {
        _entries.Add(new PermissionAccessMapEntry
        {
            TenantId = tenantId,
            PermissionId = permissionId,
            RoleNames = roleNames?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [],
            RoleIds = roleIds?.Where(x => x != Guid.Empty).Distinct().ToList() ?? []
        });
        return this;
    }

    public IReadOnlyList<PermissionAccessMapEntry> Build()
        => _entries.ToList();
}
