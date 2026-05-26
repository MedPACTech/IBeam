using IBeam.Identity.Interfaces;

namespace IBeam.Identity.Events;

public sealed class NoOpAuthLifecycleHook : IAuthLifecycleHook
{
    public Task OnAuthUserCreatedAsync(AuthUserCreatedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnTenantCreatedAsync(TenantCreatedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnTenantUserLinkedAsync(TenantUserLinkedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
}

