using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Authorization;

public sealed class NoOpPermissionAccessStore : IPermissionAccessStore
{
    public Task<PermissionGrantSet> ResolveGrantsAsync(
        Guid tenantId,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default)
        => Task.FromResult(PermissionGrantSet.Empty);

    public Task<IReadOnlyList<PermissionRoleMap>> GetMappingsAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PermissionRoleMap>>(Array.Empty<PermissionRoleMap>());

    public Task<PermissionRoleMap> UpsertByPermissionNameAsync(
        Guid tenantId,
        string permissionName,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default)
        => Task.FromResult(new PermissionRoleMap(
            tenantId,
            permissionName,
            null,
            roleNames,
            roleIds,
            true,
            DateTimeOffset.UtcNow));

    public Task<PermissionRoleMap> UpsertByPermissionIdAsync(
        Guid tenantId,
        Guid permissionId,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default)
        => Task.FromResult(new PermissionRoleMap(
            tenantId,
            null,
            permissionId,
            roleNames,
            roleIds,
            true,
            DateTimeOffset.UtcNow));

    public Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default)
        => Task.CompletedTask;
}
