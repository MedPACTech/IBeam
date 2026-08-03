using System.Text.Json.Serialization;

namespace IBeam.Ai;

public sealed class IBeamMcpProtectedResourceMetadata
{
    [JsonPropertyName("resource")]
    public required string Resource { get; init; }

    [JsonPropertyName("authorization_servers")]
    public required string[] AuthorizationServers { get; init; }

    [JsonPropertyName("scopes_supported")]
    public required string[] ScopesSupported { get; init; }

    [JsonPropertyName("bearer_methods_supported")]
    public string[] BearerMethodsSupported { get; init; } = ["header"];

    [JsonPropertyName("resource_name")]
    public required string ResourceName { get; init; }
}

public sealed class IBeamMcpEndpointMetadata
{
    public IBeamMcpEndpointMetadata(IEnumerable<string> requiredScopes)
    {
        RequiredScopes = requiredScopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> RequiredScopes { get; }
}
