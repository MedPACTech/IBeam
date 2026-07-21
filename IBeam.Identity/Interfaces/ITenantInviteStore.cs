using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantInviteStore
{
    Task<TenantInviteRecord> CreateAsync(TenantInviteRecord invite, CancellationToken ct = default);
    Task<IReadOnlyList<TenantInviteRecord>> ListForTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantInviteRecord?> GetAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default);
    Task<TenantInviteRecord?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<TenantInviteRecord> UpdateAsync(TenantInviteRecord invite, CancellationToken ct = default);
}
