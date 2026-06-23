using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IBeam.Ai;

public static class EndpointRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RouteHandlerBuilder MapIBeamMcp(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/api/mcp",
        string? authorizationPolicy = null)
    {
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

        return string.IsNullOrWhiteSpace(authorizationPolicy)
            ? route.RequireAuthorization()
            : route.RequireAuthorization(authorizationPolicy);
    }
}
