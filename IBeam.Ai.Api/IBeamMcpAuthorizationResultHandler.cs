using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace IBeam.Ai;

internal sealed class IBeamMcpAuthorizationResultHandler(
    IOptions<IBeamMcpOAuthOptions> options) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        await _fallback.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);

        var endpointMetadata = context.GetEndpoint()?.Metadata.GetMetadata<IBeamMcpEndpointMetadata>();
        if (endpointMetadata is null ||
            !options.Value.Enabled ||
            !IBeamMcpOAuthUris.TryCreateMetadataUri(options.Value, out var metadataUri))
        {
            return;
        }

        if (authorizeResult.Challenged)
        {
            AppendChallenge(context, $"Bearer resource_metadata=\"{metadataUri.AbsoluteUri}\"");
            return;
        }

        if (authorizeResult.Forbidden)
        {
            var scopes = endpointMetadata.RequiredScopes.Count > 0
                ? endpointMetadata.RequiredScopes
                : options.Value.SupportedScopes;
            var scope = string.Join(' ', scopes);
            AppendChallenge(
                context,
                $"Bearer error=\"insufficient_scope\", scope=\"{scope}\", resource_metadata=\"{metadataUri.AbsoluteUri}\"");
        }
    }

    private static void AppendChallenge(HttpContext context, string challenge)
    {
        var existing = context.Response.Headers.WWWAuthenticate;
        if (existing.Any(value => string.Equals(value, challenge, StringComparison.Ordinal)))
            return;

        context.Response.Headers.WWWAuthenticate = StringValues.Concat(existing, challenge);
    }
}
