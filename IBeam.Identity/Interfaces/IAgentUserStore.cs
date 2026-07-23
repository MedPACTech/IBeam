using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IAgentUserStore
{
    Task<AgentUserRecord> CreateAsync(AgentUserRecord agentUser, CancellationToken ct = default);
    Task<IReadOnlyList<AgentUserRecord>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<AgentUserRecord?> GetAsync(Guid tenantId, Guid agentUserId, CancellationToken ct = default);
    Task<AgentUserRecord> UpdateAsync(AgentUserRecord agentUser, CancellationToken ct = default);
    Task<AgentUserCredentialBindingRecord> BindCredentialAsync(AgentUserCredentialBindingRecord binding, CancellationToken ct = default);
    Task<IReadOnlyList<AgentUserCredentialBindingRecord>> ListCredentialBindingsAsync(Guid tenantId, Guid agentUserId, CancellationToken ct = default);
    Task<AgentUserCredentialBindingRecord?> GetCredentialBindingAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default);
    Task RevokeCredentialBindingAsync(Guid tenantId, Guid agentUserId, Guid credentialId, Guid? revokedByUserId, CancellationToken ct = default);
}
