using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IOAuthClientAdministrationService
{
    Task<OAuthClientCreatedResult> CreateAsync(Guid tenantId, CreateOAuthClientRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<OAuthClientInfo>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task<OAuthClientInfo> GetAsync(Guid tenantId, string clientId, CancellationToken ct = default);
    Task<OAuthClientInfo> UpdateAsync(Guid tenantId, string clientId, UpdateOAuthClientRequest request, CancellationToken ct = default);
    Task<OAuthClientSecretRotatedResult> RotateSecretAsync(Guid tenantId, string clientId, CancellationToken ct = default);
    Task<OAuthClientInfo> DisableAsync(Guid tenantId, string clientId, CancellationToken ct = default);
    Task<OAuthClientInfo> RevokeAsync(Guid tenantId, string clientId, CancellationToken ct = default);
}
