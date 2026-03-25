namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

public interface ITenantRoleService
{
    Task<IReadOnlyList<TenantRole>> GetRolesAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantRole?> GetRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);
    Task<TenantRole> CreateRoleAsync(Guid tenantId, string name, CancellationToken ct = default);
    Task<TenantRole> UpdateRoleAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct = default);
    Task DeleteRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    Task<UserTenantRoleAssignment> GrantRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default);
    Task<UserTenantRoleAssignment> RevokeRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default);
    Task<IReadOnlyList<TenantRole>> GetRolesForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
