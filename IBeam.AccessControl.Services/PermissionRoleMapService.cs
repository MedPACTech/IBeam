using IBeam.Services.Abstractions;

namespace IBeam.AccessControl.Services;

[IBeamOperation("accesscontrol.permissionroles")]
public sealed class PermissionRoleMapService : IPermissionRoleMapService
{
    private readonly IPermissionRoleMapStore _store;
    private readonly IServiceOperationExecutor _operations;

    public PermissionRoleMapService(IPermissionRoleMapStore store, IServiceOperationExecutor? operations = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("accesscontrol.permissionroles.list")]
    public async Task<IReadOnlyList<PermissionRoleMapInfo>> ListMappingsAsync(Guid tenantId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListMappingsCoreAsync(tenantId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<PermissionRoleMapInfo>> ListMappingsCoreAsync(Guid tenantId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        var records = await _store.ListMappingsAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(PermissionRoleMapInfo.FromRecord).ToList();
    }

    [IBeamOperation("accesscontrol.permissionroles.upsert.name")]
    public async Task<PermissionRoleMapInfo> UpsertByPermissionNameAsync(
        Guid tenantId,
        string permissionName,
        UpsertPermissionRoleMapRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => UpsertByPermissionNameCoreAsync(tenantId, permissionName, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    private async Task<PermissionRoleMapInfo> UpsertByPermissionNameCoreAsync(
        Guid tenantId,
        string permissionName,
        UpsertPermissionRoleMapRequest request,
        CancellationToken ct)
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

    [IBeamOperation("accesscontrol.permissionroles.upsert.id")]
    public async Task<PermissionRoleMapInfo> UpsertByPermissionIdAsync(
        Guid tenantId,
        Guid permissionId,
        UpsertPermissionRoleMapRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => UpsertByPermissionIdCoreAsync(tenantId, permissionId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = permissionId },
            ct).ConfigureAwait(false);

    private async Task<PermissionRoleMapInfo> UpsertByPermissionIdCoreAsync(
        Guid tenantId,
        Guid permissionId,
        UpsertPermissionRoleMapRequest request,
        CancellationToken ct)
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

    [IBeamOperation("accesscontrol.permissionroles.delete.name")]
    public Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => DeleteByPermissionNameCoreAsync(tenantId, permissionName, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct);

    private Task DeleteByPermissionNameCoreAsync(Guid tenantId, string permissionName, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        return _store.DeleteByPermissionNameAsync(tenantId, NormalizePermissionName(permissionName), ct);
    }

    [IBeamOperation("accesscontrol.permissionroles.delete.id")]
    public Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => DeleteByPermissionIdCoreAsync(tenantId, permissionId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = permissionId },
            ct);

    private Task DeleteByPermissionIdCoreAsync(Guid tenantId, Guid permissionId, CancellationToken ct)
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
