using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Users;

public sealed class IdentityUserExtensionCoordinator<TUserExtension> : IIdentityUserExtensionCoordinator
    where TUserExtension : class, IIdentityUserExtension
{
    private readonly IIdentityUserExtensionResolver<TUserExtension> _resolver;

    public IdentityUserExtensionCoordinator(IIdentityUserExtensionResolver<TUserExtension> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public Task EnsureExtensionAsync(IdentityUser identityUser, UserExtensionContext context, CancellationToken ct = default)
        => _resolver.EnsureAsync(identityUser, context, ct);

    public Task OnUserCreatedAsync(IdentityUser identityUser, UserExtensionContext context, CancellationToken ct = default)
        => _resolver.EnsureAsync(identityUser, context, ct);
}
