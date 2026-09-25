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

public interface IOAuthDeviceAuthorizationStore
{
    Task<OAuthDeviceAuthorizationRecord> CreateAsync(
        OAuthDeviceAuthorizationRecord authorization,
        CancellationToken ct = default);

    Task<OAuthDeviceAuthorizationRecord?> GetByDeviceCodeHashAsync(
        string deviceCodeHash,
        CancellationToken ct = default);

    Task<OAuthDeviceAuthorizationRecord?> GetByUserCodeAsync(
        string userCode,
        CancellationToken ct = default);

    Task<OAuthDeviceAuthorizationRecord?> TryApproveAsync(
        string userCode,
        Guid userId,
        Guid tenantId,
        IReadOnlyList<string> grantedScopes,
        DateTimeOffset approvedUtc,
        CancellationToken ct = default);

    Task<OAuthDeviceAuthorizationRecord?> TryDenyAsync(
        string userCode,
        DateTimeOffset deniedUtc,
        CancellationToken ct = default);

    Task<OAuthDeviceAuthorizationRecord?> TryConsumeAsync(
        string deviceCodeHash,
        DateTimeOffset consumedUtc,
        CancellationToken ct = default);

    // Best-effort: used only to enforce RFC 8628's `slow_down` politeness response. A lost update
    // under a race just means a poll that arrived a moment early isn't caught - never a security
    // concern, so this doesn't need the optimistic-concurrency retry loop the other mutators use.
    Task<bool> RecordPollAsync(
        string deviceCodeHash,
        DateTimeOffset polledUtc,
        int minIntervalSeconds,
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
