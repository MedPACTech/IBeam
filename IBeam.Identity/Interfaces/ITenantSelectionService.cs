namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

public interface ITenantSelectionService
{
    Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default);
    Task<TokenResult> SelectTenantAsync(TenantSelectionRequest request, CancellationToken ct = default);
}
