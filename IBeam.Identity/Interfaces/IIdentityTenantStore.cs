using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IIdentityTenantStore
{
    Task<IdentityTenant?> FindByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<IdentityTenant> CreateAsync(IdentityTenant tenant, CancellationToken ct = default);
    Task<IdentityTenant> UpdateAsync(IdentityTenant tenant, CancellationToken ct = default);
    Task<IdentityTenant> SetStatusAsync(Guid tenantId, string status, CancellationToken ct = default);
}
