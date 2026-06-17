using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Users;

public sealed class NoOpIdentityUserExtensionCoordinator : IIdentityUserExtensionCoordinator
{
    public Task EnsureExtensionAsync(IdentityUser identityUser, UserExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnUserCreatedAsync(IdentityUser identityUser, UserExtensionContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}
