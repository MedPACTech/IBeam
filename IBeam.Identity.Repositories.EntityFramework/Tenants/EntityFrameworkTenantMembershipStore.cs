using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;

namespace IBeam.Identity.Repositories.EntityFramework.Tenants;

public sealed class EntityFrameworkTenantMembershipStore : ITenantMembershipStore
{
    public Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantInfo>>(Array.Empty<TenantInfo>());

    public Task<TenantInfo?> GetTenantForUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<TenantInfo?>(null);

    public Task<Guid?> GetDefaultTenantIdAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<Guid?>(null);

    public Task SetDefaultTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
        => Task.CompletedTask;
}
