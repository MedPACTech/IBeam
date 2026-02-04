using IBeam.Identity.Core.Auth.Contracts;
using IBeam.Identity.Core.Tenants;

namespace IBeam.Identity.Repositories.EntityFramework.Tenants;

public sealed class EntityFrameworkTenantMembershipStore : ITenantMembershipStore
{
    public Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(string userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantInfo>>(Array.Empty<TenantInfo>());

    public Task<TenantInfo?> GetTenantForUserAsync(string userId, Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<TenantInfo?>(null);

    public Task<Guid?> GetDefaultTenantIdAsync(string userId, CancellationToken ct = default)
        => Task.FromResult<Guid?>(null);

    public Task SetDefaultTenantAsync(string userId, Guid tenantId, CancellationToken ct = default)
        => Task.CompletedTask;
}
