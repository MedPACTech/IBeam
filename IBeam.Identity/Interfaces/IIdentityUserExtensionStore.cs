using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IIdentityUserExtensionStore<TUserExtension>
    where TUserExtension : class, IIdentityUserExtension
{
    Task<TUserExtension?> FindByUserIdAsync(Guid userId, Guid? tenantId, CancellationToken ct = default);

    Task<TUserExtension> CreateAsync(
        IdentityUser identityUser,
        UserExtensionContext context,
        CancellationToken ct = default);

    Task<TUserExtension> UpdateFromIdentityUserAsync(
        TUserExtension extension,
        IdentityUser identityUser,
        UserExtensionContext context,
        CancellationToken ct = default)
        => Task.FromResult(extension);
}
