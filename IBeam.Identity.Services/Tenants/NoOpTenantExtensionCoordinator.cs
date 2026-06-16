using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Tenants;

public sealed class NoOpTenantExtensionCoordinator : ITenantExtensionCoordinator
{
    public Task EnsureExtensionAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnTenantCreatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnTenantUpdatedAsync(IdentityTenant identityTenant, IdentityTenant? previousTenant, TenantExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnTenantActivatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnTenantDeactivatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}
