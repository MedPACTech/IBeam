using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace IBeam.Ai;

public sealed class HttpAgentToolContextFactory : IAgentToolContextFactory
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpAgentToolContextFactory(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public AgentToolContext Create(IServiceProvider services)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        return new AgentToolContext(
            user,
            services,
            ResolveAgentKey(user),
            ResolveGuid(user, AgentClaimTypes.TenantId, AgentClaimTypes.AlternateTenantId),
            ResolveGuid(user, AgentClaimTypes.ApiCredentialId),
            ResolveGuid(user, AgentClaimTypes.AgentUserId),
            ResolveString(user, AgentClaimTypes.AgentUserName),
            ResolveString(user, AgentClaimTypes.AgentType));
    }

    private static string? ResolveAgentKey(ClaimsPrincipal user)
    {
        if (ResolveGuid(user, AgentClaimTypes.AgentUserId) is not null)
        {
            var agentUserKey = user.FindAll(AgentClaimTypes.AlternateAgentKey)
                .Select(x => x.Value)
                .LastOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(agentUserKey))
                return agentUserKey.Trim();
        }

        foreach (var claimType in new[]
                 {
                     AgentClaimTypes.AgentKey,
                     AgentClaimTypes.AlternateAgentKey,
                     "agent",
                     "apiAgentKey",
                     "apiAgentId"
                 })
        {
            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static Guid? ResolveGuid(ClaimsPrincipal user, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = user.FindFirst(claimType)?.Value;
            if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
                return parsed;
        }

        return null;
    }

    private static string? ResolveString(ClaimsPrincipal user, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
