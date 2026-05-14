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
public sealed class RolesControllerOptionsTests
{
    private static readonly Guid TenantId = Guid.Parse("225925cc-995e-4584-a63b-4f2cb4f38f6f");

    [TestMethod]
    public async Task CreateRole_ReturnsForbidden_WhenTenantRoleCreationDisabled()
    {
        var sut = CreateController(new RoleManagementOptions { AllowTenantRoleCreation = false });

        var result = await sut.CreateRole(TenantId, new UpsertRoleRequest { Name = "ClinicalAdmin" }, CancellationToken.None);

        Assert.IsInstanceOfType<ObjectResult>(result);
        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task CreateRole_ReturnsOk_WhenTenantRoleCreationEnabled()
    {
        var sut = CreateController(new RoleManagementOptions { AllowTenantRoleCreation = true });

        var result = await sut.CreateRole(TenantId, new UpsertRoleRequest { Name = "ClinicalAdmin" }, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    private static RolesController CreateController(RoleManagementOptions options)
    {
        var controller = new RolesController(
            new FakeTenantRoleService(),
            new StaticOptionsSnapshot<RoleManagementOptions>(options));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("tid", TenantId.ToString("D")),
                    new Claim("role", "admin")
                ], "unit-test"))
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

    private sealed class FakeTenantRoleService : ITenantRoleService
    {
        public Task<IReadOnlyList<TenantRole>> GetRolesAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantRole>>(Array.Empty<TenantRole>());

        public Task<TenantRole?> GetRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
            => Task.FromResult<TenantRole?>(null);

        public Task<TenantRole> CreateRoleAsync(Guid tenantId, string name, CancellationToken ct = default)
            => Task.FromResult(new TenantRole(
                TenantId: tenantId,
                RoleId: Guid.NewGuid(),
                Name: name,
                IsSystem: false,
                IsActive: true,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow));

        public Task<TenantRole> UpdateRoleAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct = default)
            => Task.FromResult(new TenantRole(
                TenantId: tenantId,
                RoleId: roleId,
                Name: name,
                IsSystem: false,
                IsActive: true,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow));

        public Task DeleteRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<UserTenantRoleAssignment> GrantRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
            => Task.FromResult(new UserTenantRoleAssignment(
                tenantId,
                userId,
                roleIds.Select(x => new TenantRole(
                    TenantId: tenantId,
                    RoleId: x,
                    Name: $"role-{x:D}",
                    IsSystem: false,
                    IsActive: true,
                    CreatedAt: DateTimeOffset.UtcNow)).ToList()));

        public Task<UserTenantRoleAssignment> RevokeRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
            => Task.FromResult(new UserTenantRoleAssignment(tenantId, userId, Array.Empty<TenantRole>()));

        public Task<IReadOnlyList<TenantRole>> GetRolesForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantRole>>(Array.Empty<TenantRole>());
    }
}
