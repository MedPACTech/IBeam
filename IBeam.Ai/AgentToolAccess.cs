using System.Security.Claims;

namespace IBeam.Ai;

public sealed record AgentToolAccessResult(bool Allowed, string? Reason = null)
{
    public static AgentToolAccessResult Allow()
        => new(true);

    public static AgentToolAccessResult Deny(string reason)
        => new(false, reason);
}

public interface IAgentToolAccessPolicy
{
    ValueTask<AgentToolAccessResult> CanAccessAsync(
        AgentToolContext context,
        AgentToolDefinition tool,
        CancellationToken cancellationToken = default);
}
