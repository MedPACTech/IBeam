using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantExtensionCoordinator
{
    Task EnsureExtensionAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default);
    Task OnTenantCreatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default);
    Task OnTenantUpdatedAsync(IdentityTenant identityTenant, IdentityTenant? previousTenant, TenantExtensionContext context, CancellationToken ct = default);
    Task OnTenantActivatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default);
    Task OnTenantDeactivatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default);
}
