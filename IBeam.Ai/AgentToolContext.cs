using System.Security.Claims;

namespace IBeam.Ai;

public sealed record AgentToolContext(
    ClaimsPrincipal User,
    IServiceProvider Services,
    string? AgentKey,
    Guid? TenantId,
    Guid? ApiCredentialId,
    Guid? AgentUserId = null,
    string? AgentUserName = null,
    string? AgentType = null);
