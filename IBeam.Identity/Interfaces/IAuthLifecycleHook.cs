using IBeam.Identity.Events;

namespace IBeam.Identity.Interfaces;

public interface IAuthLifecycleHook
{
    Task OnAuthUserCreatedAsync(AuthUserCreatedEvent evt, CancellationToken ct = default);
    Task OnTenantCreatedAsync(TenantCreatedEvent evt, CancellationToken ct = default);
    Task OnTenantUserLinkedAsync(TenantUserLinkedEvent evt, CancellationToken ct = default);
}

