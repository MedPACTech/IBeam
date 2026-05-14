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
public sealed class PermissionMappingsControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("f4b9dbfc-3fc6-4b66-b62a-4ece6e2d663e");
    private static readonly Guid RoleId = Guid.Parse("4e0f3475-df09-4e36-b0f1-02fb5d2eb01b");
    private static readonly Guid PermissionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [TestMethod]
    public async Task GetCatalog_ReturnsOk_ForTenantAdmin()
    {
        var sut = CreateController(
            roleOptions: new RoleManagementOptions(),
            permissionOptions: new PermissionAccessOptions(),
            catalog: new[]
            {
                new ExposedPermission("Example.Read", null, "attribute:method", "ExampleService.Read")
            });

        var result = await sut.GetCatalog(TenantId, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task UpsertByName_ReturnsConflict_WhenRepositoryModeDisabled()
    {
        var sut = CreateController(
            roleOptions: new RoleManagementOptions
            {
                PermissionMode = PermissionManagementMode.Configuration,
                AllowTenantPermissionMapMutation = true
            },
            permissionOptions: new PermissionAccessOptions());

        var result = await sut.UpsertByName(
            TenantId,
            new PermissionMapByNameRequest { PermissionName = "Example.Read", RoleIds = [RoleId] },
            CancellationToken.None);

        Assert.IsInstanceOfType<ObjectResult>(result);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task UpsertByName_ReturnsForbidden_WhenMutationDisabled()
    {
        var sut = CreateController(
            roleOptions: new RoleManagementOptions
            {
                PermissionMode = PermissionManagementMode.Hybrid,
                AllowTenantPermissionMapMutation = false
            },
            permissionOptions: new PermissionAccessOptions());

        var result = await sut.UpsertByName(
            TenantId,
            new PermissionMapByNameRequest { PermissionName = "Example.Read", RoleIds = [RoleId] },
            CancellationToken.None);

        Assert.IsInstanceOfType<ObjectResult>(result);
        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task UpsertById_Succeeds_InHybridMode()
    {
        var store = new FakePermissionAccessStore();
        var sut = CreateController(
            roleOptions: new RoleManagementOptions
            {
                PermissionMode = PermissionManagementMode.Hybrid,
                AllowTenantPermissionMapMutation = true
            },
            permissionOptions: new PermissionAccessOptions(),
            store: store);

        var result = await sut.UpsertById(
            TenantId,
            new PermissionMapByIdRequest { PermissionId = PermissionId, RoleNames = ["admin"] },
            CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.AreEqual(1, store.Maps.Count);
        Assert.AreEqual(PermissionId, store.Maps[0].PermissionId);
    }

    private static PermissionMappingsController CreateController(
        RoleManagementOptions roleOptions,
        PermissionAccessOptions permissionOptions,
        IPermissionAccessStore? store = null,
        IReadOnlyList<ExposedPermission>? catalog = null)
    {
        var controller = new PermissionMappingsController(
            store ?? new FakePermissionAccessStore(),
            new FakePermissionCatalogProvider(catalog ?? Array.Empty<ExposedPermission>()),
            new StaticOptionsSnapshot<RoleManagementOptions>(roleOptions),
            new StaticOptionsSnapshot<PermissionAccessOptions>(permissionOptions));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildAdminPrincipal(TenantId)
            }
        };

        return controller;
    }

    private static ClaimsPrincipal BuildAdminPrincipal(Guid tenantId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tid", tenantId.ToString("D")),
            new Claim("role", "admin")
        ], "unit-test"));
    }

    private sealed class StaticOptionsSnapshot<T> : IOptionsSnapshot<T>
        where T : class
    {
        public StaticOptionsSnapshot(T value) => Value = value;
        public T Value { get; }
        public T Get(string? name) => Value;
    }

    private sealed class FakePermissionCatalogProvider : IPermissionCatalogProvider
    {
        private readonly IReadOnlyList<ExposedPermission> _catalog;

        public FakePermissionCatalogProvider(IReadOnlyList<ExposedPermission> catalog)
        {
            _catalog = catalog;
        }

        public Task<IReadOnlyList<ExposedPermission>> GetExposedPermissionsAsync(CancellationToken ct = default)
            => Task.FromResult(_catalog);
    }

    private sealed class FakePermissionAccessStore : IPermissionAccessStore
    {
        public List<PermissionRoleMap> Maps { get; } = [];

        public Task<PermissionGrantSet> ResolveGrantsAsync(Guid tenantId, IReadOnlyList<string> permissionNames, IReadOnlyList<Guid> permissionIds, CancellationToken ct = default)
            => Task.FromResult(PermissionGrantSet.Empty);

        public Task<IReadOnlyList<PermissionRoleMap>> GetMappingsAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PermissionRoleMap>>(Maps);

        public Task<PermissionRoleMap> UpsertByPermissionNameAsync(Guid tenantId, string permissionName, IReadOnlyList<string> roleNames, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
        {
            var map = new PermissionRoleMap(tenantId, permissionName, null, roleNames, roleIds, true, DateTimeOffset.UtcNow);
            Maps.RemoveAll(x => string.Equals(x.PermissionName, permissionName, StringComparison.OrdinalIgnoreCase));
            Maps.Add(map);
            return Task.FromResult(map);
        }

        public Task<PermissionRoleMap> UpsertByPermissionIdAsync(Guid tenantId, Guid permissionId, IReadOnlyList<string> roleNames, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
        {
            var map = new PermissionRoleMap(tenantId, null, permissionId, roleNames, roleIds, true, DateTimeOffset.UtcNow);
            Maps.RemoveAll(x => x.PermissionId == permissionId);
            Maps.Add(map);
            return Task.FromResult(map);
        }

        public Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default)
        {
            Maps.RemoveAll(x => string.Equals(x.PermissionName, permissionName, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default)
        {
            Maps.RemoveAll(x => x.PermissionId == permissionId);
            return Task.CompletedTask;
        }
    }
}
