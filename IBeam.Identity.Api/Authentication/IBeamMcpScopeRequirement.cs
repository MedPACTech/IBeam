using System.Security.Claims;
using IBeam.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Api.Authentication;

internal sealed class IBeamMcpScopeRequirement : IAuthorizationRequirement;

internal sealed class IBeamMcpScopeAuthorizationHandler(
    IOptions<IBeamMcpOAuthOptions> options) : AuthorizationHandler<IBeamMcpScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        IBeamMcpScopeRequirement requirement)
    {
        var requiredScope = string.IsNullOrWhiteSpace(options.Value.RequiredScope)
            ? IBeamMcpAuthenticationDefaults.DefaultRequiredScope
            : options.Value.RequiredScope.Trim();

        if (McpOAuthClaims.HasValue(context.User, requiredScope))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

internal static class McpOAuthClaims
{
    private static readonly string[] ScopeClaimTypes =
    [
        ClaimTypes.Role,
        "role",
        "roles",
        "scope",
        "scopes",
        "scp"
    ];

    public static bool HasValue(ClaimsPrincipal principal, string expected)
    {
        return principal.Claims
            .Where(claim => ScopeClaimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase))
            .SelectMany(claim => Split(claim.Value))
            .Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase));
    }

    public static void Normalize(ClaimsIdentity identity)
    {
        var canonical = identity.Claims
            .Where(claim => ScopeClaimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase))
            .SelectMany(claim => Split(claim.Value))
            .Concat(identity.FindAll("tool").Select(claim => $"tool:{claim.Value}"))
            .Where(value => value.Contains(':', StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var value in canonical)
        {
            if (!identity.HasClaim("role", value))
                identity.AddClaim(new Claim("role", value));
            if (!identity.HasClaim("scope", value))
                identity.AddClaim(new Claim("scope", value));
        }
    }

    private static IEnumerable<string> Split(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
