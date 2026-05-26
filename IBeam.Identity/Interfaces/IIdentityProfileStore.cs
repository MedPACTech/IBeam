using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IIdentityProfileStore
{
    Task<IdentityProfileExtensions> GetAsync(Guid userId, CancellationToken ct = default);
    Task<IdentityProfileExtensions> UpsertAsync(Guid userId, IReadOnlyDictionary<string, string> attributes, CancellationToken ct = default);
    Task RemoveKeysAsync(Guid userId, IReadOnlyCollection<string> keys, CancellationToken ct = default);
}
