using System.Security.Claims;
using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
                SetAsDefault = true,
                DisplayName = "Ada Lovelace",
                Email = "ada@example.com",
                PhoneNumber = "+16145551212"
            },
            CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.IsNotNull(roles.LastBootstrapRequest);
        Assert.AreEqual(TenantId, roles.LastBootstrapRequest.TenantId);
        Assert.AreEqual(UserId, roles.LastBootstrapRequest.UserId);
        CollectionAssert.Contains(roles.LastBootstrapRequest.RoleNames!.ToList(), "Editor");
        Assert.IsTrue(roles.LastBootstrapRequest.SetAsDefault);
        Assert.AreEqual("Ada Lovelace", roles.LastBootstrapRequest.UserDisplayName);
        Assert.AreEqual("ada@example.com", roles.LastBootstrapRequest.UserEmail);
        Assert.AreEqual("+16145551212", roles.LastBootstrapRequest.UserPhoneNumber);
    }

    [TestMethod]
    public async Task LinkTenantUser_ReturnsOk_ForConfiguredPermissionClaim()
    {
        var accessOptions = new IBeamAccessControlOptions
        {
            TenantUserManagementPermissionNames = ["identity.users.invite"]
        };
        var roles = new FakeTenantRoleService();
        var sut = CreateController(role: "member", permission: "identity.users.invite", roles: roles, accessOptions: accessOptions);

        var result = await sut.LinkTenantUser(
            TenantId,
            new LinkTenantUserRequest
            {
                UserId = UserId,
                RoleNames = ["Editor"]
            },
            CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.IsNotNull(roles.LastBootstrapRequest);
    }

    private static TenantUsersController CreateController(
        string role,
        string? permission = null,
        FakeTenantMembershipStore? memberships = null,
        FakeTenantRoleService? roles = null,
        IBeamAccessControlOptions? accessOptions = null)
    {
        var controller = new TenantUsersController(
            memberships ?? new FakeTenantMembershipStore(),
            roles ?? new FakeTenantRoleService(),
            new StaticOptionsSnapshot<IBeamAccessControlOptions>(accessOptions ?? new IBeamAccessControlOptions()));

        var claims = new List<Claim>
        {
            new("uid", Guid.NewGuid().ToString("D")),
            new("tid", TenantId.ToString("D")),
            new("role", role)
        };

        if (!string.IsNullOrWhiteSpace(permission))
            claims.Add(new Claim("permission", permission));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "unit-test"))
            }
        };

        return controller;
    }

    private sealed class StaticOptionsSnapshot<T> : IOptionsSnapshot<T>
        where T : class
    {
        public StaticOptionsSnapshot(T value) => Value = value;
        public T Value { get; }
        public T Get(string? name) => Value;
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

        public Task<TenantRole> CreateRoleAsync(Guid tenantId, string name, CancellationToken ct = default, string? description = null)
            => Task.FromResult(CreateRole(tenantId, Guid.NewGuid(), name));

        public Task<TenantRole> UpdateRoleAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct = default, string? description = null)
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
