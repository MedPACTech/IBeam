using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IIdentityUserExtensionCoordinator
{
    Task EnsureExtensionAsync(IdentityUser identityUser, UserExtensionContext context, CancellationToken ct = default);
    Task OnUserCreatedAsync(IdentityUser identityUser, UserExtensionContext context, CancellationToken ct = default);
}
