using System.Collections.Concurrent;

namespace IBeam.AccessControl.Services;

public sealed class InMemoryResourceAccessStore : IResourceAccessStore
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid GrantId), ResourceAccessGrantRecord> _grants = [];

    public Task<IReadOnlyList<ResourceAccessGrantRecord>> ListGrantsAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ResourceAccessGrantRecord>>(
            _grants.Values
                .Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.ResourceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Subject.SubjectType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Subject.SubjectId, StringComparer.OrdinalIgnoreCase)
                .ToList());

    public Task<ResourceAccessGrantRecord?> GetGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
    {
        _grants.TryGetValue((tenantId, grantId), out var grant);
        return Task.FromResult(grant);
    }

    public Task<ResourceAccessGrantRecord> UpsertGrantAsync(ResourceAccessGrantRecord grant, CancellationToken ct = default)
    {
        _grants[(grant.TenantId, grant.GrantId)] = grant;
        return Task.FromResult(grant);
    }

    public Task DeleteGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
    {
        _grants.TryRemove((tenantId, grantId), out _);
        return Task.CompletedTask;
    }
}
