using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IApiCredentialStore
{
    Task<ApiCredentialRecord> CreateAsync(ApiCredentialRecord credential, CancellationToken ct = default);
    Task<IReadOnlyList<ApiCredentialRecord>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<ApiCredentialRecord?> GetAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default);
    Task<ApiCredentialRecord> UpdateRolesAsync(
        Guid tenantId,
        Guid credentialId,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> roleNames,
        CancellationToken ct = default);
    Task<ApiCredentialRecord> RevokeAsync(
        Guid tenantId,
        Guid credentialId,
        Guid? revokedByUserId,
        string? reason,
        CancellationToken ct = default);
    Task TouchLastUsedAsync(Guid tenantId, Guid credentialId, DateTimeOffset usedUtc, string? ipAddress, CancellationToken ct = default);
}
