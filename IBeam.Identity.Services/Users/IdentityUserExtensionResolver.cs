using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Users;

public sealed class IdentityUserExtensionResolver<TUserExtension> : IIdentityUserExtensionResolver<TUserExtension>
    where TUserExtension : class, IIdentityUserExtension
{
    private readonly IIdentityUserExtensionStore<TUserExtension> _store;

    public IdentityUserExtensionResolver(IIdentityUserExtensionStore<TUserExtension> store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<TUserExtension?> ResolveAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");

        return _store.FindByUserIdAsync(userId, tenantId, ct);
    }

    public async Task<TUserExtension> EnsureAsync(
        IdentityUser identityUser,
        UserExtensionContext context,
        CancellationToken ct = default)
    {
        ValidateIdentityUser(identityUser);
        context ??= CreateContext(identityUser, UserExtensionOperations.Ensure, null);

        if (context.UserId != identityUser.UserId)
            throw new IdentityValidationException("User extension context user id does not match identity user.");

        var existing = await _store.FindByUserIdAsync(identityUser.UserId, context.TenantId, ct).ConfigureAwait(false);
        if (existing is null)
            return await _store.CreateAsync(identityUser, context, ct).ConfigureAwait(false);

        return await _store.UpdateFromIdentityUserAsync(existing, identityUser, context, ct).ConfigureAwait(false);
    }

    private static UserExtensionContext CreateContext(IdentityUser identityUser, string operation, Guid? tenantId)
        => UserExtensionContext.Create(
            operation,
            identityUser.UserId,
            tenantId,
            identityUser.Email,
            identityUser.PhoneNumber,
            identityUser.DisplayName);

    private static void ValidateIdentityUser(IdentityUser identityUser)
    {
        ArgumentNullException.ThrowIfNull(identityUser);

        if (identityUser.UserId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
    }
}
