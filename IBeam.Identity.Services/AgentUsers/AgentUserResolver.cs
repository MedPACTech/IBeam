using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.AgentUsers;

public sealed class AgentUserResolver : IAgentUserResolver
{
    private readonly IEnumerable<IAgentUserStore> _stores;

    public AgentUserResolver(IEnumerable<IAgentUserStore> stores)
    {
        _stores = stores;
    }

    public async Task<ResolvedAgentUser?> ResolveForCredentialAsync(
        Guid tenantId,
        Guid credentialId,
        CancellationToken ct = default)
    {
        var store = _stores.FirstOrDefault();
        if (store is null || tenantId == Guid.Empty || credentialId == Guid.Empty)
            return null;

        var binding = await store.GetCredentialBindingAsync(tenantId, credentialId, ct).ConfigureAwait(false);
        if (binding is null || !binding.IsActive)
            return null;

        var agentUser = await store.GetAsync(tenantId, binding.AgentUserId, ct).ConfigureAwait(false);
        if (agentUser is null || !agentUser.IsActive)
            return null;

        return new ResolvedAgentUser(
            AgentUserInfo.FromRecord(agentUser),
            AgentUserCredentialBindingInfo.FromRecord(binding));
    }
}
