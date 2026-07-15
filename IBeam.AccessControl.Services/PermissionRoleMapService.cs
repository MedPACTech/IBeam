namespace IBeam.AccessControl.Services;

public sealed class PermissionRoleMapService : IPermissionRoleMapService
{
    private readonly IPermissionRoleMapStore _store;

    public PermissionRoleMapService(IPermissionRoleMapStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<PermissionRoleMapInfo>> ListMappingsAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var records = await _store.ListMappingsAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(PermissionRoleMapInfo.FromRecord).ToList();
    }

    public async Task<PermissionRoleMapInfo> UpsertByPermissionNameAsync(
        Guid tenantId,
        string permissionName,
        UpsertPermissionRoleMapRequest request,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        var record = await _store.UpsertByPermissionNameAsync(
            tenantId,
            NormalizePermissionName(permissionName),
            NormalizeRoleNames(request.RoleNames),
            NormalizeRoleIds(request.RoleIds),
            ct).ConfigureAwait(false);

        return PermissionRoleMapInfo.FromRecord(record);
    }

    public async Task<PermissionRoleMapInfo> UpsertByPermissionIdAsync(
        Guid tenantId,
        Guid permissionId,
        UpsertPermissionRoleMapRequest request,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidatePermissionId(permissionId);
        ArgumentNullException.ThrowIfNull(request);
        var record = await _store.UpsertByPermissionIdAsync(
            tenantId,
            permissionId,
            NormalizeRoleNames(request.RoleNames),
            NormalizeRoleIds(request.RoleIds),
            ct).ConfigureAwait(false);

        return PermissionRoleMapInfo.FromRecord(record);
    }

    public Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        return _store.DeleteByPermissionNameAsync(tenantId, NormalizePermissionName(permissionName), ct);
    }

    public Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidatePermissionId(permissionId);
        return _store.DeleteByPermissionIdAsync(tenantId, permissionId, ct);
    }

    internal static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new AccessControlException("tenantId is required.");
    }

    internal static void ValidatePermissionId(Guid permissionId)
    {
        if (permissionId == Guid.Empty)
            throw new AccessControlException("permissionId is required.");
    }

    internal static string NormalizePermissionName(string permissionName)
    {
        if (string.IsNullOrWhiteSpace(permissionName))
            throw new AccessControlException("permissionName is required.");

        return permissionName.Trim();
    }

    internal static IReadOnlyList<string> NormalizeRoleNames(IReadOnlyList<string>? roleNames)
        => (roleNames ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static IReadOnlyList<Guid> NormalizeRoleIds(IReadOnlyList<Guid>? roleIds)
        => (roleIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
}
