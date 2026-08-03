using IBeam.Identity.Models;

namespace IBeam.Identity.Options;

public sealed class OAuthAuthorizationServerOptions
{
    public const string SectionName = "IBeam:Identity:OAuthServer";

    public bool Enabled { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public int AuthorizationCodeLifetimeMinutes { get; set; } = 5;
    public bool ClientIdMetadataDocumentsEnabled { get; set; } = true;
    public bool DynamicClientRegistrationEnabled { get; set; }
    public List<OAuthClientRegistrationOptions> Clients { get; set; } = [];

    public void Validate()
    {
        Issuer = Issuer.Trim();
        if (Enabled)
            OAuthServerUriValidation.RequireIssuer(Issuer, $"{SectionName}:{nameof(Issuer)}");

        if (AuthorizationCodeLifetimeMinutes is < 1 or > 15)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(AuthorizationCodeLifetimeMinutes)} must be between 1 and 15 minutes.");
        }

        Clients ??= [];
        foreach (var client in Clients)
            client.NormalizeAndValidate();

        var duplicate = Clients
            .GroupBy(x => x.ClientId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"OAuth client id '{duplicate.Key}' is configured more than once.");
    }
}

public sealed class OAuthClientRegistrationOptions
{
    public string ClientId { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ClientType { get; set; } = OAuthClientTypes.Public;
    public List<string> RedirectUris { get; set; } = [];
    public List<string> AllowedGrantTypes { get; set; } = [OAuthGrantTypes.AuthorizationCode];
    public List<string> AllowedScopes { get; set; } = [];
    public List<string> AllowedResources { get; set; } = [];
    public bool RequirePkce { get; set; } = true;
    public string Status { get; set; } = OAuthClientStatuses.Active;
    public string? ClientSecretHash { get; set; }
    public string? ClientSecretHashAlgorithm { get; set; }
    public DateTimeOffset? ClientSecretExpiresUtc { get; set; }

    public void NormalizeAndValidate()
    {
        ClientId = RequireValue(ClientId, nameof(ClientId), 200);
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? ClientId : DisplayName.Trim();
        ClientType = RequireValue(ClientType, nameof(ClientType), 32).ToLowerInvariant();
        Status = RequireValue(Status, nameof(Status), 32).ToLowerInvariant();
        ClientSecretHash = NormalizeOptional(ClientSecretHash);
        ClientSecretHashAlgorithm = NormalizeOptional(ClientSecretHashAlgorithm);

        if (ClientType is not OAuthClientTypes.Public and not OAuthClientTypes.Confidential)
            throw new InvalidOperationException($"OAuth client '{ClientId}' has unsupported client type '{ClientType}'.");

        if (Status is not OAuthClientStatuses.Active and not OAuthClientStatuses.Disabled and not OAuthClientStatuses.Revoked)
            throw new InvalidOperationException($"OAuth client '{ClientId}' has unsupported status '{Status}'.");

        RedirectUris = NormalizeList(RedirectUris, StringComparer.Ordinal);
        AllowedGrantTypes = NormalizeList(AllowedGrantTypes, StringComparer.Ordinal)
            .Select(x => x.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        AllowedScopes = NormalizeList(AllowedScopes, StringComparer.Ordinal);
        AllowedResources = NormalizeList(AllowedResources, StringComparer.Ordinal);

        if (AllowedGrantTypes.Count == 0)
            throw new InvalidOperationException($"OAuth client '{ClientId}' must allow at least one grant type.");

        var unsupportedGrant = AllowedGrantTypes.FirstOrDefault(x => !OAuthGrantTypes.Supported.Contains(x));
        if (unsupportedGrant is not null)
            throw new InvalidOperationException($"OAuth client '{ClientId}' has unsupported grant type '{unsupportedGrant}'.");

        if (AllowedGrantTypes.Contains(OAuthGrantTypes.AuthorizationCode, StringComparer.Ordinal) && RedirectUris.Count == 0)
            throw new InvalidOperationException($"OAuth client '{ClientId}' must configure at least one redirect URI.");

        if (AllowedGrantTypes.Contains(OAuthGrantTypes.RefreshToken, StringComparer.Ordinal) &&
            !AllowedGrantTypes.Contains(OAuthGrantTypes.AuthorizationCode, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"OAuth client '{ClientId}' cannot enable refresh_token without authorization_code.");
        }

        if (ClientType == OAuthClientTypes.Public)
        {
            if (!RequirePkce)
                throw new InvalidOperationException($"Public OAuth client '{ClientId}' must require PKCE.");
            if (ClientSecretHash is not null)
                throw new InvalidOperationException($"Public OAuth client '{ClientId}' cannot configure a client secret hash.");
            if (AllowedGrantTypes.Contains(OAuthGrantTypes.ClientCredentials, StringComparer.Ordinal))
                throw new InvalidOperationException($"Public OAuth client '{ClientId}' cannot use client_credentials.");
        }
        else if (ClientSecretHash is null)
        {
            throw new InvalidOperationException($"Confidential OAuth client '{ClientId}' must configure a client secret hash.");
        }

        foreach (var redirectUri in RedirectUris)
            OAuthServerUriValidation.RequireRedirectUri(redirectUri, ClientType, ClientId);
        foreach (var resource in AllowedResources)
            OAuthServerUriValidation.RequireResource(resource, ClientId);
        foreach (var scope in AllowedScopes)
            RequireScopeToken(scope, ClientId);
    }

    private static string RequireValue(string? value, string name, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
            throw new InvalidOperationException($"OAuth client {name} must be between 1 and {maximumLength} characters.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> NormalizeList(IEnumerable<string>? values, IEqualityComparer<string> comparer) =>
        (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(comparer)
            .ToList();

    private static void RequireScopeToken(string scope, string clientId)
    {
        if (scope.Length > 200 || scope.Any(c => char.IsWhiteSpace(c) || c is '"' or '\\'))
            throw new InvalidOperationException($"OAuth client '{clientId}' has invalid scope token '{scope}'.");
    }
}

internal static class OAuthServerUriValidation
{
    public static void RequireIssuer(string value, string settingName)
    {
        if (!TryCreateSecureWebUri(value, out var uri) || uri.Fragment.Length > 0 || uri.Query.Length > 0)
            throw new InvalidOperationException($"{settingName} must be an absolute HTTPS URI or an HTTP loopback URI without query or fragment.");
    }

    public static void RequireRedirectUri(string value, string clientType, string clientId)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new InvalidOperationException($"OAuth client '{clientId}' has invalid redirect URI '{value}'.");

        if (TryCreateSecureWebUri(value, out _))
            return;

        var privateUseScheme = clientType == OAuthClientTypes.Public &&
            !string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "data", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "javascript", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        if (!privateUseScheme)
            throw new InvalidOperationException($"OAuth client '{clientId}' has insecure redirect URI '{value}'.");
    }

    public static void RequireResource(string value, string clientId)
    {
        if (!TryCreateSecureWebUri(value, out var uri) || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new InvalidOperationException($"OAuth client '{clientId}' has invalid resource URI '{value}'.");
    }

    private static bool TryCreateSecureWebUri(string value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!))
            return false;

        return uri.Scheme == Uri.UriSchemeHttps ||
               (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
