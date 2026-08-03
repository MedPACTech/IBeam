namespace IBeam.Ai;

internal static class IBeamMcpOAuthUris
{
    internal const string WellKnownPath = "/.well-known/oauth-protected-resource";

    public static bool TryCreateMetadataUri(IBeamMcpOAuthOptions options, out Uri metadataUri)
    {
        metadataUri = null!;
        if (!TryCreateResourceUri(options.ResourceUri, out var resourceUri))
            return false;

        var path = resourceUri.AbsolutePath == "/"
            ? WellKnownPath
            : WellKnownPath + resourceUri.AbsolutePath.TrimEnd('/');

        metadataUri = new UriBuilder(resourceUri.Scheme, resourceUri.Host, resourceUri.Port, path).Uri;
        return true;
    }

    public static bool TryCreateMetadata(IBeamMcpOAuthOptions options, out IBeamMcpProtectedResourceMetadata metadata)
    {
        metadata = null!;
        if (!options.Enabled ||
            !TryCreateResourceUri(options.ResourceUri, out var resourceUri) ||
            !TryCreateHttpsUri(options.AuthorizationServerUri, out var authorizationServerUri))
        {
            return false;
        }

        metadata = new IBeamMcpProtectedResourceMetadata
        {
            Resource = resourceUri.AbsoluteUri.TrimEnd('/'),
            AuthorizationServers = [authorizationServerUri.AbsoluteUri.TrimEnd('/')],
            ScopesSupported = options.SupportedScopes
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ResourceName = string.IsNullOrWhiteSpace(options.ResourceName)
                ? "IBeam MCP"
                : options.ResourceName.Trim()
        };

        return true;
    }

    private static bool TryCreateResourceUri(string value, out Uri uri)
    {
        if (!TryCreateHttpsUri(value, out uri))
            return false;

        return string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.Query);
    }

    private static bool TryCreateHttpsUri(string value, out Uri uri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
