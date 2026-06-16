using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Tenants;

public sealed class NoOpTenantMetadataProvider : ITenantMetadataProvider
{
    public Task<TenantMetadata?> GetTenantMetadataAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<TenantMetadata?>(null);
}
