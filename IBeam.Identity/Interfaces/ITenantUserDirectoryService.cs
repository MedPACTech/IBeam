using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantUserDirectoryService
{
    Task<IReadOnlyList<TenantUserDirectoryItem>> ListAsync(
        Guid tenantId,
        TenantUserDirectoryRequest? request = null,
        CancellationToken ct = default);
}

