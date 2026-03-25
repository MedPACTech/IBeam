using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IPermissionAccessStore
{
    Task<PermissionGrantSet> ResolveGrantsAsync(
        Guid tenantId,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PermissionRoleMap>> GetMappingsAsync(Guid tenantId, CancellationToken ct = default);

    Task<PermissionRoleMap> UpsertByPermissionNameAsync(
        Guid tenantId,
        string permissionName,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default);

    Task<PermissionRoleMap> UpsertByPermissionIdAsync(
        Guid tenantId,
        Guid permissionId,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default);

    Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default);
    Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default);
}
