using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IPermissionCatalogProvider
{
    Task<IReadOnlyList<ExposedPermission>> GetExposedPermissionsAsync(CancellationToken ct = default);
}
