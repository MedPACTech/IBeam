using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantMetadataProvider
{
    Task<TenantMetadata?> GetTenantMetadataAsync(Guid tenantId, CancellationToken ct = default);
}
