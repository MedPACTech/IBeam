using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IApiCredentialRoleCatalogProvider
{
    Task<IReadOnlyList<ApiCredentialRoleCatalogEntry>> ListAsync(CancellationToken ct = default);
}
