using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IIdentityTenantService
{
    Task<IdentityTenant?> FindByIdAsync(Guid tenantId, CancellationToken ct = default);

    Task<IdentityTenant> CreateAsync(
        string name,
        Guid? tenantId = null,
        TenantExtensionContext? context = null,
        CancellationToken ct = default);

    Task<IdentityTenant> UpdateAsync(
        IdentityTenant tenant,
        TenantExtensionContext? context = null,
        CancellationToken ct = default);

    Task<IdentityTenant> ActivateAsync(
        Guid tenantId,
        TenantExtensionContext? context = null,
        CancellationToken ct = default);

    Task<IdentityTenant> DeactivateAsync(
        Guid tenantId,
        TenantExtensionContext? context = null,
        CancellationToken ct = default);

    Task EnsureExtensionAsync(
        Guid tenantId,
        TenantExtensionContext? context = null,
        CancellationToken ct = default);
}
