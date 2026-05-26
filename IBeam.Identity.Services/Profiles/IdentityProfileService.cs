using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Exceptions;

namespace IBeam.Identity.Services.Profiles;

public sealed class IdentityProfileService : IIdentityProfileService
{
    private readonly IIdentityProfileStore _store;

    public IdentityProfileService(IIdentityProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<IdentityProfileExtensions> GetAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");

        return _store.GetAsync(userId, ct);
    }

    public Task<IdentityProfileExtensions> UpsertAsync(Guid userId, IReadOnlyDictionary<string, string> attributes, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (attributes is null)
            throw new ArgumentNullException(nameof(attributes));

        return _store.UpsertAsync(userId, attributes, ct);
    }

    public Task RemoveKeysAsync(Guid userId, IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (keys is null)
            throw new ArgumentNullException(nameof(keys));

        return _store.RemoveKeysAsync(userId, keys, ct);
    }
}
