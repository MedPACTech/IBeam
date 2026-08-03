namespace IBeam.Identity.Models;

public sealed record OAuthAuthorizationServerMetadata(
    string Issuer,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string RevocationEndpoint,
    string JwksUri,
    string? RegistrationEndpoint,
    IReadOnlyList<string> ResponseTypesSupported,
    IReadOnlyList<string> GrantTypesSupported,
    IReadOnlyList<string> CodeChallengeMethodsSupported,
    IReadOnlyList<string> TokenEndpointAuthMethodsSupported,
    IReadOnlyList<string> ScopesSupported,
    bool ResourceIndicatorsSupported);

public sealed record OAuthClientMetadataDocument(
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> GrantTypes,
    IReadOnlyList<string> Scope,
    IReadOnlyList<string> Resources,
    string TokenEndpointAuthMethod);

public sealed class DynamicOAuthClientRegistrationRequest
{
    public string ClientName { get; set; } = string.Empty;
    public List<string> RedirectUris { get; set; } = [];
    public List<string> GrantTypes { get; set; } = [OAuthGrantTypes.AuthorizationCode];
    public string Scope { get; set; } = string.Empty;
    public List<string> Resources { get; set; } = [];
    public string TokenEndpointAuthMethod { get; set; } = "none";
}
