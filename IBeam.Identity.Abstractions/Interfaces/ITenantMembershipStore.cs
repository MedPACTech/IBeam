namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface ITenantMembershipStore
{
    Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsUserInTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
}
