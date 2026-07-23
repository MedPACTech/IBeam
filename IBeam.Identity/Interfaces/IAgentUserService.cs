using System.Security.Claims;
using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IAgentUserService
{
    Task<AgentUserInfo> CreateAsync(Guid tenantId, CreateAgentUserRequest request, Guid? createdByUserId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentUserInfo>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task<AgentUserInfo> GetAsync(Guid tenantId, Guid agentUserId, CancellationToken ct = default);
    Task<AgentUserInfo> UpdateAsync(Guid tenantId, Guid agentUserId, UpdateAgentUserRequest request, CancellationToken ct = default);
    Task<AgentUserCredentialBindingInfo> BindCredentialAsync(Guid tenantId, Guid agentUserId, BindAgentUserCredentialRequest request, Guid? createdByUserId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentUserCredentialBindingInfo>> ListCredentialBindingsAsync(Guid tenantId, Guid agentUserId, CancellationToken ct = default);
    Task RevokeCredentialBindingAsync(Guid tenantId, Guid agentUserId, Guid credentialId, Guid? revokedByUserId, CancellationToken ct = default);
    Task<AgentUserMeDto> GetCurrentAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}

public interface IAgentUserResolver
{
    Task<ResolvedAgentUser?> ResolveForCredentialAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default);
}
