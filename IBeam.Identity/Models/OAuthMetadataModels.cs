using System.Text.Json.Serialization;

namespace IBeam.Identity.Models;

/// <summary>
/// OAuth 2.0 Authorization Server Metadata (RFC 8414).
///
/// Member names are pinned with <see cref="JsonPropertyNameAttribute"/> because this is a
/// wire contract, not an internal DTO. Without them the host application's serialiser
/// policy decides the names — under the ASP.NET web defaults that yields camelCase, and a
/// conformant client then finds no authorization_endpoint, falls back to the RFC 8414
/// default of {issuer}/authorize, and fails.
/// </summary>
public sealed record OAuthAuthorizationServerMetadata(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("revocation_endpoint")] string RevocationEndpoint,
    [property: JsonPropertyName("jwks_uri")] string JwksUri,
    [property: JsonPropertyName("registration_endpoint")] string? RegistrationEndpoint,
    [property: JsonPropertyName("response_types_supported")] IReadOnlyList<string> ResponseTypesSupported,
    [property: JsonPropertyName("grant_types_supported")] IReadOnlyList<string> GrantTypesSupported,
    [property: JsonPropertyName("code_challenge_methods_supported")] IReadOnlyList<string> CodeChallengeMethodsSupported,
    [property: JsonPropertyName("token_endpoint_auth_methods_supported")] IReadOnlyList<string> TokenEndpointAuthMethodsSupported,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
    [property: JsonPropertyName("resource_indicators_supported")] bool ResourceIndicatorsSupported);

/// <summary>
/// OAuth 2.0 client metadata (RFC 7591 section 2), returned from registration and from
/// the client metadata document endpoint.
/// </summary>
public sealed record OAuthClientMetadataDocument(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string> RedirectUris,
    [property: JsonPropertyName("grant_types")] IReadOnlyList<string> GrantTypes,
    [property: JsonPropertyName("scope")] IReadOnlyList<string> Scope,
    [property: JsonPropertyName("resources")] IReadOnlyList<string> Resources,
    [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod);

/// <summary>
/// OAuth 2.0 Dynamic Client Registration request (RFC 7591).
///
/// Clients send these names, so they must bind regardless of how the host configures its
/// serialiser. Unbound fields are not a validation error — they silently default, which
/// produces a client with no redirect URIs that only fails later, at authorization.
/// </summary>
public sealed class DynamicOAuthClientRegistrationRequest
{
    [JsonPropertyName("client_name")]
    public string ClientName { get; set; } = string.Empty;

    [JsonPropertyName("redirect_uris")]
    public List<string> RedirectUris { get; set; } = [];

    [JsonPropertyName("grant_types")]
    public List<string> GrantTypes { get; set; } = [OAuthGrantTypes.AuthorizationCode];

    /// <summary>Space-delimited, per RFC 7591.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyName("resources")]
    public List<string> Resources { get; set; } = [];

    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; set; } = "none";
}
