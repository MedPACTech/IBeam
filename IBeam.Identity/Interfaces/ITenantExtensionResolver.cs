using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantExtensionResolver<TTenant>
    where TTenant : class, IIdentityTenantExtension
{
    Task<TTenant?> ResolveAsync(Guid tenantId, CancellationToken ct = default);
    Task<TTenant> EnsureAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default);
}
