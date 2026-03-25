using System.Reflection;
using System.Security.Claims;
using IBeam.Identity.Authorization;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Authorization;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class PermissionAccessAuthorizerTests
{
    private static readonly Guid TenantId = Guid.Parse("d12902b8-580f-4de9-bf81-c39e08e7f7d1");
    private static readonly Guid PermissionId = Guid.Parse("6c76f166-b130-4c80-bf7e-99d38ea1a75f");
    private static readonly Guid RoleId = Guid.Parse("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9");

    [TestMethod]
    public async Task IsAuthorizedAsync_UsesConfigurationMapping_ByPermissionName()
    {
        var opts = new PermissionAccessOptions
        {
            Mappings =
            [
                new PermissionAccessMapEntry
                {
                    TenantId = TenantId,
                    PermissionName = "SavePatient",
                    RoleNames = ["admin"]
                }
            ]
        };

        var store = CreateStoreMock(PermissionGrantSet.Empty);
        var sut = CreateSut(opts, store.Object);

        var principal = BuildPrincipal(
            new Claim("tid", TenantId.ToString("D")),
            new Claim("role", "admin"));

        var allowed = await sut.IsAuthorizedAsync(principal, GetMethod<PermissionByNameService>(nameof(PermissionByNameService.Save)));

        Assert.IsTrue(allowed);
    }

    [TestMethod]
    public async Task IsAuthorizedAsync_UsesStoreMapping_ByPermissionId()
    {
        var store = CreateStoreMock(new PermissionGrantSet(Array.Empty<string>(), new[] { RoleId }));
        var sut = CreateSut(new PermissionAccessOptions(), store.Object);

        var principal = BuildPrincipal(
            new Claim("tid", TenantId.ToString("D")),
            new Claim("rid", RoleId.ToString("D")));

        var allowed = await sut.IsAuthorizedAsync(principal, GetMethod<PermissionByIdService>(nameof(PermissionByIdService.Save)));

        Assert.IsTrue(allowed);
        store.Verify(x => x.ResolveGrantsAsync(
            TenantId,
            It.IsAny<IReadOnlyList<string>>(),
            It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(PermissionId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task IsAuthorizedAsync_MethodAllowAll_OverridesClassPermission()
    {
        var store = CreateStoreMock(PermissionGrantSet.Empty);
        var sut = CreateSut(new PermissionAccessOptions(), store.Object);

        var principal = BuildPrincipal(new Claim("sub", Guid.NewGuid().ToString("D")));
        var allowed = await sut.IsAuthorizedAsync(principal, GetMethod<MethodAllowAllPermissionService>(nameof(MethodAllowAllPermissionService.Save)));

        Assert.IsTrue(allowed);
        store.Verify(x => x.ResolveGrantsAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task IsAuthorizedAsync_WithoutPermissionAttributes_FallsBackToStaticRoleAttributes()
    {
        var store = CreateStoreMock(PermissionGrantSet.Empty);
        var sut = CreateSut(new PermissionAccessOptions(), store.Object);

        var principal = BuildPrincipal(new Claim("role", "admin"));
        var allowed = await sut.IsAuthorizedAsync(principal, GetMethod<StaticRoleService>(nameof(StaticRoleService.Save)));

        Assert.IsTrue(allowed);
        store.Verify(x => x.ResolveGrantsAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PermissionAccessAuthorizer CreateSut(PermissionAccessOptions options, IPermissionAccessStore store)
    {
        var monitor = new Mock<IOptionsMonitor<PermissionAccessOptions>>(MockBehavior.Strict);
        monitor.SetupGet(x => x.CurrentValue).Returns(options);

        var resolver = new PermissionGrantResolver(monitor.Object, store);
        return new PermissionAccessAuthorizer(new RoleAccessAuthorizer(), resolver);
    }

    private static Mock<IPermissionAccessStore> CreateStoreMock(PermissionGrantSet grants)
    {
        var store = new Mock<IPermissionAccessStore>(MockBehavior.Strict);
        store.Setup(x => x.ResolveGrantsAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(grants);
        return store;
    }

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "unit-test"));

    private static MethodInfo GetMethod<T>(string methodName)
        => typeof(T).GetMethod(methodName)!;

    [PermissionAccess("SavePatient")]
    private sealed class PermissionByNameService
    {
        public void Save() { }
    }

    private sealed class PermissionByIdService
    {
        [PermissionAccessId("6c76f166-b130-4c80-bf7e-99d38ea1a75f")]
        public void Save() { }
    }

    [PermissionAccess("ClassLevelPermission")]
    private sealed class MethodAllowAllPermissionService
    {
        [AllowAllRoleAccess]
        public void Save() { }
    }

    [RoleAccess("admin")]
    private sealed class StaticRoleService
    {
        public void Save() { }
    }
}
