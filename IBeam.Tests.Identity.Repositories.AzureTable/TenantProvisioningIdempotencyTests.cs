using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Repositories.AzureTable.Tenants;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Repositories.AzureTable;

[TestClass]
public sealed class TenantProvisioningIdempotencyTests
{
    [TestMethod]
    public async Task CreateTenantForNewUserAsync_ReturnsStableTenantId_AndBootstrapsOwnerRoles()
    {
        var userId = Guid.NewGuid();
        var roleStore = new RecordingTenantRoleStore();
        var service = new AzureTableTenantProvisioningService(
            new TableServiceClient("UseDevelopmentStorage=true"),
            Options.Create(new AzureTableIdentityOptions { StorageConnectionString = "UseDevelopmentStorage=true" }),
            roleStore);

        var first = await service.CreateTenantForNewUserAsync(userId, "adam@example.com");
        var second = await service.CreateTenantForNewUserAsync(userId, "adam@example.com");

        Assert.AreEqual(first, second);
        Assert.AreEqual(2, roleStore.Requests.Count);
        Assert.IsTrue(roleStore.Requests.All(x => x.TenantId == first));
        Assert.IsTrue(roleStore.Requests.All(x => x.UserId == userId));
        Assert.IsTrue(roleStore.Requests.All(x => x.SetAsDefault));
        CollectionAssert.AreEquivalent(
            new[] { AzureTableTenantRoleStore.OwnerRoleName, AzureTableTenantRoleStore.AdminRoleName },
            roleStore.Requests[0].RoleNames!.ToArray());
    }

    private sealed class RecordingTenantRoleStore : ITenantRoleStore
    {
        public List<TenantMembershipRoleBootstrapRequest> Requests { get; } = [];

        public Task<IReadOnlyList<TenantRole>> GetRolesAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantRole>>(Array.Empty<TenantRole>());

        public Task<TenantRole?> GetRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
            => Task.FromResult<TenantRole?>(null);

        public Task<TenantRole> CreateRoleAsync(Guid tenantId, string name, bool isSystem = false, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TenantRole> UpdateRoleAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserTenantRoleAssignment> GrantRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserTenantRoleAssignment> EnsureTenantMembershipAndGrantRolesAsync(
            TenantMembershipRoleBootstrapRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new UserTenantRoleAssignment(request.TenantId, request.UserId, Array.Empty<TenantRole>()));
        }

        public Task<UserTenantRoleAssignment> RevokeRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantRole>> GetRolesForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantRole>>(Array.Empty<TenantRole>());

        public Task EnsureDefaultRolesAsync(Guid tenantId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
