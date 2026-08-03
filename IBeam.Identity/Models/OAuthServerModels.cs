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

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        AuthorizationCode,
        RefreshToken,
        ClientCredentials
    };
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
    DateTimeOffset? RevokedUtc = null)
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
