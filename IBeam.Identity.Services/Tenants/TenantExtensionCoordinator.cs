using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Tenants;

public sealed class TenantExtensionCoordinator<TTenant> : ITenantExtensionCoordinator
    where TTenant : class, IIdentityTenantExtension
{
    private readonly ITenantExtensionResolver<TTenant> _resolver;
    private readonly IEnumerable<ITenantLifecycleHook> _hooks;

    public TenantExtensionCoordinator(
        ITenantExtensionResolver<TTenant> resolver,
        IEnumerable<ITenantLifecycleHook> hooks)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
    }

    public Task EnsureExtensionAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
        => _resolver.EnsureAsync(identityTenant, context, ct);

    public async Task OnTenantCreatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
    {
        await InvokeHooksAsync(h => h.OnTenantCreatedAsync(identityTenant, context, ct)).ConfigureAwait(false);
        await _resolver.EnsureAsync(identityTenant, context, ct).ConfigureAwait(false);
    }

    public async Task OnTenantUpdatedAsync(
        IdentityTenant identityTenant,
        IdentityTenant? previousTenant,
        TenantExtensionContext context,
        CancellationToken ct = default)
    {
        await InvokeHooksAsync(h => h.OnTenantUpdatedAsync(identityTenant, previousTenant, context, ct)).ConfigureAwait(false);
        await _resolver.EnsureAsync(identityTenant, context, ct).ConfigureAwait(false);
    }

    public async Task OnTenantActivatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
    {
        await InvokeHooksAsync(h => h.OnTenantActivatedAsync(identityTenant, context, ct)).ConfigureAwait(false);
        await _resolver.EnsureAsync(identityTenant, context, ct).ConfigureAwait(false);
    }

    public async Task OnTenantDeactivatedAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
    {
        await InvokeHooksAsync(h => h.OnTenantDeactivatedAsync(identityTenant, context, ct)).ConfigureAwait(false);
        await _resolver.EnsureAsync(identityTenant, context, ct).ConfigureAwait(false);
    }

    private async Task InvokeHooksAsync(Func<ITenantLifecycleHook, Task> invoke)
    {
        foreach (var hook in _hooks)
            await invoke(hook).ConfigureAwait(false);
    }
}
