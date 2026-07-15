using System.Collections.Concurrent;

namespace IBeam.AccessControl.Services;

public sealed class InMemoryPermissionRoleMapStore : IPermissionRoleMapStore
{
    private readonly ConcurrentDictionary<(Guid TenantId, string Key), PermissionRoleMapRecord> _mappings = [];

    public Task<PermissionGrantSet> ResolveGrantsAsync(
        Guid tenantId,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default)
    {
        var roleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleIds = new HashSet<Guid>();

        foreach (var permissionName in NormalizePermissionNames(permissionNames))
        {
            if (_mappings.TryGetValue((tenantId, NameKey(permissionName)), out var record) && record.IsActive)
                AddGrants(record, roleNames, roleIds);
        }

        foreach (var permissionId in NormalizePermissionIds(permissionIds))
        {
            if (_mappings.TryGetValue((tenantId, IdKey(permissionId)), out var record) && record.IsActive)
                AddGrants(record, roleNames, roleIds);
        }

        return Task.FromResult(new PermissionGrantSet(roleNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(), roleIds.OrderBy(x => x).ToList()));
    }

    public Task<IReadOnlyList<PermissionRoleMapRecord>> ListMappingsAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PermissionRoleMapRecord>>(
            _mappings.Values
                .Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.PermissionName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.PermissionId)
                .ToList());

    public Task<PermissionRoleMapRecord> UpsertByPermissionNameAsync(
        Guid tenantId,
        string permissionName,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default)
    {
        var normalizedName = PermissionRoleMapService.NormalizePermissionName(permissionName);
        var record = new PermissionRoleMapRecord(
            tenantId,
            normalizedName,
            null,
            PermissionRoleMapService.NormalizeRoleNames(roleNames),
            PermissionRoleMapService.NormalizeRoleIds(roleIds),
            PermissionRoleMapStatuses.Active,
            DateTimeOffset.UtcNow);

        _mappings[(tenantId, NameKey(normalizedName))] = record;
        return Task.FromResult(record);
    }

    public Task<PermissionRoleMapRecord> UpsertByPermissionIdAsync(
        Guid tenantId,
        Guid permissionId,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default)
    {
        PermissionRoleMapService.ValidatePermissionId(permissionId);
        var record = new PermissionRoleMapRecord(
            tenantId,
            null,
            permissionId,
            PermissionRoleMapService.NormalizeRoleNames(roleNames),
            PermissionRoleMapService.NormalizeRoleIds(roleIds),
            PermissionRoleMapStatuses.Active,
            DateTimeOffset.UtcNow);

        _mappings[(tenantId, IdKey(permissionId))] = record;
        return Task.FromResult(record);
    }

    public Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default)
    {
        _mappings.TryRemove((tenantId, NameKey(PermissionRoleMapService.NormalizePermissionName(permissionName))), out _);
        return Task.CompletedTask;
    }

    public Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default)
    {
        PermissionRoleMapService.ValidatePermissionId(permissionId);
        _mappings.TryRemove((tenantId, IdKey(permissionId)), out _);
        return Task.CompletedTask;
    }

    private static void AddGrants(
        PermissionRoleMapRecord record,
        HashSet<string> roleNames,
        HashSet<Guid> roleIds)
    {
        foreach (var roleName in record.RoleNames)
            roleNames.Add(roleName);
        foreach (var roleId in record.RoleIds.Where(x => x != Guid.Empty))
            roleIds.Add(roleId);
    }

    private static IReadOnlyList<string> NormalizePermissionNames(IReadOnlyList<string> values)
        => (values ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(PermissionRoleMapService.NormalizePermissionName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<Guid> NormalizePermissionIds(IReadOnlyList<Guid> values)
        => (values ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

    private static string NameKey(string permissionName)
        => $"name:{permissionName.Trim().ToUpperInvariant()}";

    private static string IdKey(Guid permissionId)
        => $"id:{permissionId:D}";
}
