using System.Security.Claims;
using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class TenantUsersControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("225925cc-995e-4584-a63b-4f2cb4f38f6f");
    private static readonly Guid UserId = Guid.Parse("84d7d971-64a6-4c21-bb5b-a120599fd19a");

    [TestMethod]
    public async Task GetTenantUsers_ReturnsForbidden_WhenCallerIsNotTenantAdmin()
    {
        var sut = CreateController(role: "member");

        var result = await sut.GetTenantUsers(TenantId, CancellationToken.None);

        Assert.IsInstanceOfType<ObjectResult>(result);
        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task LinkTenantUser_UsesRoleBootstrapService()
    {
        var roles = new FakeTenantRoleService();
        var sut = CreateController(role: "admin", roles: roles);

        var result = await sut.LinkTenantUser(
            TenantId,
            new LinkTenantUserRequest
            {
                UserId = UserId,
                RoleNames = ["Editor"],
                SetAsDefault = true
            },
            CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.IsNotNull(roles.LastBootstrapRequest);
        Assert.AreEqual(TenantId, roles.LastBootstrapRequest.TenantId);
        Assert.AreEqual(UserId, roles.LastBootstrapRequest.UserId);
        CollectionAssert.Contains(roles.LastBootstrapRequest.RoleNames!.ToList(), "Editor");
        Assert.IsTrue(roles.LastBootstrapRequest.SetAsDefault);
    }

    private static TenantUsersController CreateController(
        string role,
        FakeTenantMembershipStore? memberships = null,
        FakeTenantRoleService? roles = null)
    {
        var controller = new TenantUsersController(
            memberships ?? new FakeTenantMembershipStore(),
            roles ?? new FakeTenantRoleService());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("uid", Guid.NewGuid().ToString("D")),
                    new Claim("tid", TenantId.ToString("D")),
                    new Claim("role", role)
                ], "unit-test"))
            }
        };

        return controller;
    }

    private sealed class FakeTenantMembershipStore : ITenantMembershipStore
    {
        public Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([]);

        public Task<TenantInfo?> GetTenantForUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<TenantInfo?>(null);

        public Task<IReadOnlyList<TenantUserInfo>> GetUsersForTenantAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantUserInfo>>([]);

        public Task<TenantUserInfo?> GetUserForTenantAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<TenantUserInfo?>(null);

        public Task<Guid?> GetDefaultTenantIdAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task SetDefaultTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DisableTenantMembershipAsync(Guid tenantId, Guid userId, string? reason = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeTenantRoleService : ITenantRoleService
    {
        public TenantMembershipRoleBootstrapRequest? LastBootstrapRequest { get; private set; }

        public Task<IReadOnlyList<TenantRole>> GetRolesAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantRole>>([]);

        public Task<TenantRole?> GetRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
            => Task.FromResult<TenantRole?>(null);

        public Task<TenantRole> CreateRoleAsync(Guid tenantId, string name, CancellationToken ct = default)
            => Task.FromResult(CreateRole(tenantId, Guid.NewGuid(), name));

        public Task<TenantRole> UpdateRoleAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct = default)
            => Task.FromResult(CreateRole(tenantId, roleId, name));

        public Task DeleteRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<UserTenantRoleAssignment> GrantRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
            => Task.FromResult(new UserTenantRoleAssignment(tenantId, userId, []));

        public Task<UserTenantRoleAssignment> EnsureTenantMembershipAndGrantRolesAsync(TenantMembershipRoleBootstrapRequest request, CancellationToken ct = default)
        {
            LastBootstrapRequest = request;
            return Task.FromResult(new UserTenantRoleAssignment(
                request.TenantId,
                request.UserId,
                (request.RoleNames ?? []).Select(x => CreateRole(request.TenantId, Guid.NewGuid(), x)).ToList()));
        }

        public Task<UserTenantRoleAssignment> RevokeRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
            => Task.FromResult(new UserTenantRoleAssignment(tenantId, userId, []));

        public Task<IReadOnlyList<TenantRole>> GetRolesForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantRole>>([]);

        private static TenantRole CreateRole(Guid tenantId, Guid roleId, string name)
            => new(
                tenantId,
                roleId,
                name,
                IsSystem: false,
                IsActive: true,
                CreatedAt: DateTimeOffset.UtcNow);
    }
}
