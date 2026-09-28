namespace IBeam.Identity.Models;

public static class OAuthClientTypes
{
    public const string Public = "public";
    public const string Confidential = "confidential";
}

public static class OAuthClientStatuses
{
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string Revoked = "revoked";
}

public static class OAuthGrantTypes
{
    public const string AuthorizationCode = "authorization_code";
    public const string RefreshToken = "refresh_token";
    public const string ClientCredentials = "client_credentials";
    public const string DeviceCode = "urn:ietf:params:oauth:grant-type:device_code";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        AuthorizationCode,
        RefreshToken,
        ClientCredentials,
        DeviceCode
    };
}

public static class OAuthDeviceErrors
{
    public const string AuthorizationPending = "authorization_pending";
    public const string SlowDown = "slow_down";
    public const string ExpiredToken = "expired_token";
}

public static class OAuthCodeChallengeMethods
{
    public const string S256 = "S256";
}

public sealed record OAuthClientRecord(
    string ClientId,
    Guid? TenantId,
    string DisplayName,
    string ClientType,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> AllowedGrantTypes,
    IReadOnlyList<string> AllowedScopes,
    IReadOnlyList<string> AllowedResources,
    bool RequirePkce,
    string Status,
    string? ClientSecretHash,
    string? ClientSecretHashAlgorithm,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc = null,
    DateTimeOffset? SecretRotatedUtc = null,
    DateTimeOffset? ClientSecretExpiresUtc = null,
    DateTimeOffset? DisabledUtc = null,
    DateTimeOffset? RevokedUtc = null,
    string? DeviceVerificationUri = null)
{
    public bool IsActive =>
        string.Equals(Status, OAuthClientStatuses.Active, StringComparison.Ordinal) &&
        DisabledUtc is null &&
        RevokedUtc is null;

    public bool MatchesRedirectUri(string redirectUri) =>
        RedirectUris.Contains(redirectUri, StringComparer.Ordinal);

    public bool AllowsGrantType(string grantType) =>
        AllowedGrantTypes.Contains(grantType, StringComparer.Ordinal);

    public bool AllowsScope(string scope) =>
        AllowedScopes.Contains(scope, StringComparer.Ordinal);

    public bool AllowsResource(string resource) =>
        AllowedResources.Contains(resource, StringComparer.Ordinal);
}

public sealed record OAuthAuthorizationCodeRecord(
    string CodeHash,
    string ClientId,
    string RedirectUri,
    Guid UserId,
    Guid TenantId,
    IReadOnlyList<string> Scopes,
    string Resource,
    string CodeChallenge,
    string CodeChallengeMethod,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? ConsumedUtc = null)
{
    public bool IsUsable(DateTimeOffset now) => ConsumedUtc is null && ExpiresUtc > now;
}

public sealed record OAuthConsentRecord(
    Guid ConsentId,
    Guid UserId,
    Guid TenantId,
    string ClientId,
    string Resource,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? RevokedUtc = null)
{
    public bool IsActive => RevokedUtc is null;
}

// RFC 8628 device authorization grant. UserId/TenantId are unknown at creation time (the whole
// point is the device hasn't identified anyone yet) and are only set once a signed-in browser
// approves the pending user_code. DeviceCodeHash and UserCode are two independent secrets used by
// two different callers (the polling device vs. the approving browser), so both need their own
// lookup path in a store implementation - see AzureTableOAuthDeviceAuthorizationStore.
public sealed record OAuthDeviceAuthorizationRecord(
    string DeviceCodeHash,
    string UserCode,
    string ClientId,
    IReadOnlyList<string> Scopes,
    string Resource,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    int IntervalSeconds,
    Guid? UserId = null,
    Guid? TenantId = null,
    IReadOnlyList<string>? GrantedScopes = null,
    DateTimeOffset? ApprovedUtc = null,
    DateTimeOffset? DeniedUtc = null,
    DateTimeOffset? ConsumedUtc = null,
    DateTimeOffset? LastPolledUtc = null)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresUtc <= now;
    public bool IsDenied => DeniedUtc is not null;
    public bool IsApproved => UserId is not null && TenantId is not null && !IsDenied;
    public bool IsPending => UserId is null && !IsDenied;
    public bool IsUsable(DateTimeOffset now) => ConsumedUtc is null && !IsExpired(now) && !IsDenied;
}
