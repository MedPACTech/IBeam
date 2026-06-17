using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IIdentityUserExtensionResolver<TUserExtension>
    where TUserExtension : class, IIdentityUserExtension
{
    Task<TUserExtension?> ResolveAsync(Guid userId, Guid? tenantId, CancellationToken ct = default);
    Task<TUserExtension> EnsureAsync(IdentityUser identityUser, UserExtensionContext context, CancellationToken ct = default);
}
