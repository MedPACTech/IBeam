namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface ITenantMembershipStore
{
    Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default);
    Task<TenantInfo?> GetTenantForUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default);

    Task<Guid?> GetDefaultTenantIdAsync(Guid userId, CancellationToken ct = default);
    Task SetDefaultTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
}
