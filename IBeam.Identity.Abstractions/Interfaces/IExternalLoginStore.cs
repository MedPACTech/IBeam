using IBeam.Identity.Abstractions.Models;

namespace IBeam.Identity.Abstractions.Interfaces;

public interface IExternalLoginStore
{
    Task<ExternalLoginInfo?> FindByProviderAsync(string provider, string providerUserId, CancellationToken ct = default);
    Task<ExternalLoginInfo?> FindByUserAndProviderAsync(Guid userId, string provider, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalLoginInfo>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task LinkAsync(Guid userId, string provider, string providerUserId, string? email = null, CancellationToken ct = default);
    Task<bool> UnlinkAsync(Guid userId, string provider, CancellationToken ct = default);
}
