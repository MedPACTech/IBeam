using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IOAuthClientStore
{
    Task<OAuthClientRecord?> GetAsync(string clientId, CancellationToken ct = default);
    Task<IReadOnlyList<OAuthClientRecord>> ListByTenantAsync(Guid? tenantId, CancellationToken ct = default);
    Task<OAuthClientRecord> CreateAsync(OAuthClientRecord client, CancellationToken ct = default);
    Task<OAuthClientRecord> UpdateAsync(OAuthClientRecord client, CancellationToken ct = default);
}

public interface IOAuthAuthorizationCodeStore
{
    Task<OAuthAuthorizationCodeRecord> CreateAsync(
        OAuthAuthorizationCodeRecord authorizationCode,
        CancellationToken ct = default);

    Task<OAuthAuthorizationCodeRecord?> GetByHashAsync(
        string codeHash,
        CancellationToken ct = default);

    Task<OAuthAuthorizationCodeRecord?> TryConsumeAsync(
        string codeHash,
        DateTimeOffset consumedUtc,
        CancellationToken ct = default);
}

public interface IOAuthConsentStore
{
    Task<OAuthConsentRecord?> GetAsync(
        Guid userId,
        Guid tenantId,
        string clientId,
        string resource,
        CancellationToken ct = default);

    Task<OAuthConsentRecord> UpsertAsync(
        OAuthConsentRecord consent,
        CancellationToken ct = default);

    Task<bool> RevokeAsync(
        Guid userId,
        Guid tenantId,
        string clientId,
        string resource,
        DateTimeOffset revokedUtc,
        CancellationToken ct = default);
}
