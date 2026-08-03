using System.Security.Claims;
using IBeam.Ai;
using IBeam.Identity.Api.Authentication;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Identity.Api.DependencyInjection;

public static class McpAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamMcpAuthorization(
        this IServiceCollection services,
        string apiKeyAuthenticationScheme = ApiKeyAuthenticationDefaults.AuthenticationScheme,
        string policyName = IBeamMcpAuthenticationDefaults.AuthorizationPolicy)
    {
        services.AddAuthentication()
            .AddJwtBearer(IBeamMcpAuthenticationDefaults.OAuthAuthenticationScheme, _ => { });

        services.AddOptions<JwtBearerOptions>(IBeamMcpAuthenticationDefaults.OAuthAuthenticationScheme)
            .Configure<IOptions<JwtOptions>, IOptions<IBeamMcpOAuthOptions>, IJwtSigningKeyProvider>(
                (bearer, jwtOptions, mcpOptions, signingKeys) => ConfigureBearer(
                    bearer,
                    jwtOptions.Value,
                    mcpOptions.Value,
                    signingKeys));

        services.AddScoped<IAuthorizationHandler, IBeamMcpScopeAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(policyName, policy =>
            {
                policy.AddAuthenticationSchemes(
                    apiKeyAuthenticationScheme,
                    IBeamMcpAuthenticationDefaults.OAuthAuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new IBeamMcpScopeRequirement());
            });
        });

        return services;
    }

    private static void ConfigureBearer(
        JwtBearerOptions bearer,
        JwtOptions jwt,
        IBeamMcpOAuthOptions mcp,
        IJwtSigningKeyProvider signingKeys)
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = mcp.ResourceUri,
            IssuerSigningKeyResolver = (_, _, keyId, _) => signingKeys
                .GetValidationKeys()
                .Where(key => string.IsNullOrWhiteSpace(keyId) ||
                              string.Equals(key.KeyId, keyId, StringComparison.Ordinal)),
            NameClaimType = "sub",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds)
        };
        bearer.Events = new JwtBearerEvents
        {
            OnTokenValidated = ValidateClientAsync
        };
    }

    private static async Task ValidateClientAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        var mcp = context.HttpContext.RequestServices.GetRequiredService<IOptions<IBeamMcpOAuthOptions>>().Value;
        var clientId = principal.FindFirstValue(AgentClaimTypes.OAuthClientId) ??
                       principal.FindFirstValue(AgentClaimTypes.OAuthAuthorizedParty);
        var resource = principal.FindFirstValue("resource");
        var tenantValue = principal.FindFirstValue(AgentClaimTypes.TenantId) ??
                          principal.FindFirstValue(AgentClaimTypes.AlternateTenantId);

        if (string.IsNullOrWhiteSpace(clientId) ||
            !Guid.TryParse(tenantValue, out var tenantId) ||
            !string.Equals(resource, mcp.ResourceUri, StringComparison.Ordinal))
        {
            context.Fail("The OAuth token is not bound to this MCP resource.");
            return;
        }

        var clients = context.HttpContext.RequestServices.GetRequiredService<IOAuthClientStore>();
        var client = await clients.GetAsync(clientId, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (client is null || !client.IsActive ||
            (client.TenantId is not null && client.TenantId != tenantId) ||
            !client.AllowsResource(mcp.ResourceUri))
        {
            context.Fail("The OAuth client is not active for this MCP resource.");
            return;
        }

        if (principal.Identity is ClaimsIdentity identity)
            McpOAuthClaims.Normalize(identity);
    }
}
