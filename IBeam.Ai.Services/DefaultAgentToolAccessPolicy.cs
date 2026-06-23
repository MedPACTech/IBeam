using System.Security.Claims;

namespace IBeam.Ai;

public sealed class DefaultAgentToolAccessPolicy : IAgentToolAccessPolicy
{
    private static readonly string[] ClaimTypesToInspect =
    [
        ClaimTypes.Role,
        "role",
        "roles",
        "scope",
        "scopes",
        "scp",
        "permission",
        "permissions"
    ];

    public ValueTask<AgentToolAccessResult> CanAccessAsync(
        AgentToolContext context,
        AgentToolDefinition tool,
        CancellationToken cancellationToken = default)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return ValueTask.FromResult(AgentToolAccessResult.Deny("The current request is not authenticated."));

        if (tool.RequiredScopes.Count == 0)
            return ValueTask.FromResult(AgentToolAccessResult.Allow());

        foreach (var scope in tool.RequiredScopes)
        {
            if (HasClaimValue(context.User, scope))
                return ValueTask.FromResult(AgentToolAccessResult.Allow());
        }

        return ValueTask.FromResult(AgentToolAccessResult.Deny("The current agent is not allowed to call this tool."));
    }

    private static bool HasClaimValue(ClaimsPrincipal user, string expected)
    {
        if (user.IsInRole(expected))
            return true;

        foreach (var claim in user.Claims.Where(x => ClaimTypesToInspect.Contains(x.Type, StringComparer.OrdinalIgnoreCase)))
        {
            foreach (var value in SplitClaimValue(claim.Value))
            {
                if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitClaimValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var item in value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return item;
    }
}
