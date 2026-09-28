using IBeam.Identity.Models;

namespace IBeam.Identity.Options;

public sealed class OAuthAuthorizationServerOptions
{
    public const string SectionName = "IBeam:Identity:OAuthServer";

    public bool Enabled { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public int AuthorizationCodeLifetimeMinutes { get; set; } = 5;
    public int DeviceCodeLifetimeMinutes { get; set; } = 15;
    public int DeviceCodePollIntervalSeconds { get; set; } = 5;
    public bool ClientIdMetadataDocumentsEnabled { get; set; } = true;
    public bool DynamicClientRegistrationEnabled { get; set; }
    public int DynamicRegistrationRequestsPerMinute { get; set; } = 10;
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
        if (DynamicRegistrationRequestsPerMinute is < 1 or > 1000)
            throw new InvalidOperationException($"{SectionName}:{nameof(DynamicRegistrationRequestsPerMinute)} must be between 1 and 1000.");
        if (DeviceCodeLifetimeMinutes is < 1 or > 30)
            throw new InvalidOperationException($"{SectionName}:{nameof(DeviceCodeLifetimeMinutes)} must be between 1 and 30 minutes.");
        if (DeviceCodePollIntervalSeconds is < 5 or > 60)
            throw new InvalidOperationException($"{SectionName}:{nameof(DeviceCodePollIntervalSeconds)} must be between 5 and 60 seconds.");

        Clients ??= [];
        foreach (var client in Clients)
            // Configuration cannot express an empty list: a JSON "AllowedGrantTypes": [] and an omitted
            // key are both invisible to the binder (Exists() is false for each). So on this path an
            // absent grant list can only mean "not specified", which RFC 7591 §2 says defaults to
            // authorization_code. The programmatic paths keep rejecting it — see NormalizeAndValidate.
            client.NormalizeAndValidate(applyDefaultGrantTypes: true);

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
    /// <summary>
    /// Grant types this client may use. Empty by default ON PURPOSE (IBM-0072): these options are
    /// populated with IConfiguration.Bind, which ADDS to a collection rather than replacing it, so a
    /// non-empty initializer can never be narrowed by configuration — only widened. A client configured
    /// for device_code alone would silently also allow authorization_code, and an operator reading the
    /// config file would have no way to know. The default is applied in NormalizeAndValidate instead.
    /// </summary>
    public List<string> AllowedGrantTypes { get; set; } = [];
    public List<string> AllowedScopes { get; set; } = [];
    public List<string> AllowedResources { get; set; } = [];
    public bool RequirePkce { get; set; } = true;
    public string Status { get; set; } = OAuthClientStatuses.Active;
    public string? ClientSecretHash { get; set; }
    public string? ClientSecretHashAlgorithm { get; set; }
    public DateTimeOffset? ClientSecretExpiresUtc { get; set; }
    public string? DeviceVerificationUri { get; set; }

    /// <summary>
    /// Normalizes and validates this registration.
    /// </summary>
    /// <param name="applyDefaultGrantTypes">
    /// When true, an empty <see cref="AllowedGrantTypes"/> becomes <c>authorization_code</c> instead of
    /// being rejected — the behaviour RFC 7591 §2 defines for a client that does not specify grant types.
    /// Set only on the configuration-bound path, where an empty list is indistinguishable from an
    /// omitted one. A caller registering a client programmatically CAN say "no grant types", and meaning
    /// it should not silently earn them authorization_code, so that path still refuses.
    /// </param>
    public void NormalizeAndValidate(bool applyDefaultGrantTypes = false)
    {
        ClientId = RequireValue(ClientId, nameof(ClientId), 200);
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? ClientId : DisplayName.Trim();
        ClientType = RequireValue(ClientType, nameof(ClientType), 32).ToLowerInvariant();
        Status = RequireValue(Status, nameof(Status), 32).ToLowerInvariant();
        ClientSecretHash = NormalizeOptional(ClientSecretHash);
        ClientSecretHashAlgorithm = NormalizeOptional(ClientSecretHashAlgorithm);
        DeviceVerificationUri = NormalizeOptional(DeviceVerificationUri);

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

        // The default lives here rather than in the property initializer, so that configuration replaces
        // it instead of being appended to it (IBM-0072). A client that names its grants gets exactly
        // those; one that names none still gets what it always got.
        if (AllowedGrantTypes.Count == 0)
        {
            if (!applyDefaultGrantTypes)
                throw new InvalidOperationException($"OAuth client '{ClientId}' must allow at least one grant type.");

            AllowedGrantTypes = [OAuthGrantTypes.AuthorizationCode];
        }

        var unsupportedGrant = AllowedGrantTypes.FirstOrDefault(x => !OAuthGrantTypes.Supported.Contains(x));
        if (unsupportedGrant is not null)
            throw new InvalidOperationException($"OAuth client '{ClientId}' has unsupported grant type '{unsupportedGrant}'.");

        if (AllowedGrantTypes.Contains(OAuthGrantTypes.AuthorizationCode, StringComparer.Ordinal) && RedirectUris.Count == 0)
            throw new InvalidOperationException($"OAuth client '{ClientId}' must configure at least one redirect URI.");

        if (AllowedGrantTypes.Contains(OAuthGrantTypes.DeviceCode, StringComparer.Ordinal))
        {
            if (DeviceVerificationUri is null)
                throw new InvalidOperationException($"OAuth client '{ClientId}' must configure a device verification URI to use the device_code grant.");
            OAuthServerUriValidation.RequireResource(DeviceVerificationUri, ClientId);
        }

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
