namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface ITenantSelectionService
{
    Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default);
    Task<TenantSelectionResult> SelectTenantAsync(TenantSelectionRequest request, CancellationToken ct = default);
}
