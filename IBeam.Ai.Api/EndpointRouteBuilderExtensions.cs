using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Ai;

public static class EndpointRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RouteHandlerBuilder MapIBeamMcp(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/api/mcp",
        string? authorizationPolicy = null)
    {
        endpoints.MapIBeamMcpProtectedResourceMetadata(pattern);

        var route = endpoints.MapPost(pattern, async (
            HttpContext httpContext,
            IAgentMcpService mcp,
            CancellationToken cancellationToken) =>
        {
            JsonDocument payload;
            try
            {
                payload = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                var response = new McpJsonRpcResponse
                {
                    Id = null,
                    Error = new McpJsonRpcError
                    {
                        Code = McpErrorCodes.InvalidRequest,
                        Message = "Request body must be valid JSON."
                    }
                };

                return Results.Json(response, JsonOptions, statusCode: StatusCodes.Status400BadRequest);
            }

            using (payload)
            {
                var result = await mcp.HandleAsync(payload.RootElement, cancellationToken).ConfigureAwait(false);
                if (!result.HasResponse)
                    return Results.NoContent();

                object body = result.IsBatch
                    ? result.Responses
                    : result.Responses[0];

                return Results.Json(body, JsonOptions);
            }
        });

        route.WithMetadata(new IBeamMcpEndpointMetadata(GetConfiguredScopes(endpoints)));

        return string.IsNullOrWhiteSpace(authorizationPolicy)
            ? route.RequireAuthorization()
            : route.RequireAuthorization(authorizationPolicy);
    }

    public static IEndpointRouteBuilder MapIBeamMcpProtectedResourceMetadata(
        this IEndpointRouteBuilder endpoints,
        string mcpPattern = "/api/mcp")
    {
        static IResult GetMetadata(IOptions<IBeamMcpOAuthOptions> options)
        {
            return IBeamMcpOAuthUris.TryCreateMetadata(options.Value, out var metadata)
                ? Results.Json(metadata, JsonOptions)
                : Results.NotFound();
        }

        endpoints.MapGet(IBeamMcpOAuthUris.WellKnownPath, GetMetadata).AllowAnonymous();

        var path = NormalizeMcpPath(mcpPattern);
        if (path.Length > 0)
            endpoints.MapGet(IBeamMcpOAuthUris.WellKnownPath + path, GetMetadata).AllowAnonymous();

        return endpoints;
    }

    private static string[] GetConfiguredScopes(IEndpointRouteBuilder endpoints)
    {
        return endpoints.ServiceProvider.GetService<IOptions<IBeamMcpOAuthOptions>>()?
            .Value.SupportedScopes ?? ["tool:mcp"];
    }

    private static string NormalizeMcpPath(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return string.Empty;

        var path = pattern.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return path.TrimEnd('/');
    }
}
