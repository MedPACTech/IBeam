using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantExtensionStore<TTenant>
    where TTenant : class, IIdentityTenantExtension
{
    Task<TTenant?> FindByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<TTenant> CreateAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default);

    Task<TTenant> UpdateFromIdentityTenantAsync(
        TTenant tenant,
        IdentityTenant identityTenant,
        TenantExtensionContext context,
        CancellationToken ct = default)
        => Task.FromResult(tenant);
}
