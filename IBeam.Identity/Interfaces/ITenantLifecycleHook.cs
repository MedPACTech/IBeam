using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantLifecycleHook
{
    Task OnTenantCreatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnTenantUpdatedAsync(
        IdentityTenant identityTenant,
        IdentityTenant? previousTenant,
        TenantExtensionContext context,
        CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnTenantActivatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnTenantDeactivatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}
