namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

public interface ITenantMembershipStore
{
    Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default);
    Task<TenantInfo?> GetTenantForUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<TenantUserInfo>> GetUsersForTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantUserInfo?> GetUserForTenantAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task<Guid?> GetDefaultTenantIdAsync(Guid userId, CancellationToken ct = default);
    Task SetDefaultTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task DisableTenantMembershipAsync(Guid tenantId, Guid userId, string? reason = null, CancellationToken ct = default);
}
