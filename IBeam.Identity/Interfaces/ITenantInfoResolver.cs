using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantInfoResolver
{
    Task<TenantInfo?> EnrichAsync(TenantInfo? tenant, CancellationToken ct = default);
    Task<IReadOnlyList<TenantInfo>> EnrichAsync(IReadOnlyList<TenantInfo> tenants, CancellationToken ct = default);
}
