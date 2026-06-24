using System.Security.Claims;
using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class TenantsControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("225925cc-995e-4584-a63b-4f2cb4f38f6f");
    private static readonly Guid UserId = Guid.Parse("84d7d971-64a6-4c21-bb5b-a120599fd19a");

    [TestMethod]
    public async Task GetTenant_ReturnsForbidden_WhenCallerIsNotTenantAdmin()
    {
        var sut = CreateController(role: "member");

        var result = await sut.GetTenant(TenantId, CancellationToken.None);

        Assert.IsInstanceOfType<ObjectResult>(result);
        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task CreateTenant_CreatesTenantAndLinksCurrentUser()
    {
        var tenants = new FakeIdentityTenantService();
        var roles = new FakeTenantRoleService();
        var sut = CreateController(role: "admin", tenants: tenants, roles: roles);

        var result = await sut.CreateTenant(
            new CreateIdentityTenantRequest
            {
                Name = "New Workspace",
                TenantId = TenantId,
                CurrentUserRoleNames = ["Owner"]
            },
            CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.AreEqual("New Workspace", tenants.LastCreatedName);
        Assert.IsNotNull(roles.LastBootstrapRequest);
        Assert.AreEqual(TenantId, roles.LastBootstrapRequest.TenantId);
        Assert.AreEqual(UserId, roles.LastBootstrapRequest.UserId);
    }

    private static TenantsController CreateController(
        string role,
        FakeIdentityTenantService? tenants = null,
        FakeTenantRoleService? roles = null)
    {
        var controller = new TenantsController(
            tenants ?? new FakeIdentityTenantService(),
            roles ?? new FakeTenantRoleService());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("uid", UserId.ToString("D")),
                    new Claim("tid", TenantId.ToString("D")),
                    new Claim("role", role)
                ], "unit-test"))
            }
        };

        return controller;
    }

    private sealed class FakeIdentityTenantService : IIdentityTenantService
    {
        public string? LastCreatedName { get; private set; }

        public Task<IdentityTenant?> FindByIdAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IdentityTenant?>(new IdentityTenant(
                tenantId,
                "Workspace",
                "WORKSPACE",
                IdentityTenantStatuses.Active));

        public Task<IdentityTenant> CreateAsync(string name, Guid? tenantId = null, TenantExtensionContext? context = null, CancellationToken ct = default)
        {
            LastCreatedName = name;
            return Task.FromResult(new IdentityTenant(
                tenantId ?? Guid.NewGuid(),
                name,
                IdentityTenant.NormalizeName(name),
                IdentityTenantStatuses.Active));
        }

        public Task<IdentityTenant> UpdateAsync(IdentityTenant tenant, TenantExtensionContext? context = null, CancellationToken ct = default)
            => Task.FromResult(tenant);

        public Task<IdentityTenant> ActivateAsync(Guid tenantId, TenantExtensionContext? context = null, CancellationToken ct = default)
            => FindByIdAsync(tenantId, ct).ContinueWith(t => t.Result!, ct);

        public Task<IdentityTenant> DeactivateAsync(Guid tenantId, TenantExtensionContext? context = null, CancellationToken ct = default)
            => FindByIdAsync(tenantId, ct).ContinueWith(t => t.Result! with { Status = IdentityTenantStatuses.Disabled }, ct);

        public Task EnsureExtensionAsync(Guid tenantId, TenantExtensionContext? context = null, CancellationToken ct = default)
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
            return Task.FromResult(new UserTenantRoleAssignment(request.TenantId, request.UserId, []));
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
