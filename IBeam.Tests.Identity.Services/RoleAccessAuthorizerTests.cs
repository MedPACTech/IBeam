using System.Security.Claims;
using IBeam.Identity.Authorization;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Services.Authorization;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class RoleAccessAuthorizerTests
{
    private static readonly Guid AdminRoleId = Guid.Parse("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9");

    [TestMethod]
    public void IsAuthorized_ClassLevelRoleName_MatchesRoleClaim()
    {
        var principal = BuildPrincipal(new Claim("role", "owner"));
        var method = typeof(ClassLevelRoleService).GetMethod(nameof(ClassLevelRoleService.Save))!;

        var sut = new RoleAccessAuthorizer();
        var allowed = sut.IsAuthorized(principal, method);

        Assert.IsTrue(allowed);
    }

    [TestMethod]
    public void IsAuthorized_MethodLevelRoleName_OverridesClassLevelRequirement()
    {
        var principal = BuildPrincipal(new Claim("role", "owner"));
        var method = typeof(MethodOverrideRoleService).GetMethod(nameof(MethodOverrideRoleService.Save))!;

        var sut = new RoleAccessAuthorizer();
        var allowed = sut.IsAuthorized(principal, method);

        Assert.IsFalse(allowed);
    }

    [TestMethod]
    public void IsAuthorized_RoleIdClaim_MatchesRoleAccessIdAttribute()
    {
        var principal = BuildPrincipal(new Claim("rid", AdminRoleId.ToString("D")));
        var method = typeof(RoleIdService).GetMethod(nameof(RoleIdService.Save))!;

        var sut = new RoleAccessAuthorizer();
        var allowed = sut.IsAuthorized(principal, method);

        Assert.IsTrue(allowed);
    }

    [TestMethod]
    public void EnsureAuthorized_WhenNotAuthorized_ThrowsUnauthorized()
    {
        var principal = BuildPrincipal(new Claim("role", "viewer"));
        var method = typeof(ClassLevelRoleService).GetMethod(nameof(ClassLevelRoleService.Save))!;

        var sut = new RoleAccessAuthorizer();
        Assert.ThrowsExactly<IdentityUnauthorizedException>(() => sut.EnsureAuthorized(principal, method));
    }

    [TestMethod]
    public void IsAuthorized_ClassLevelAllowAll_AllowsAuthenticatedWithoutRoleClaims()
    {
        var principal = BuildPrincipal(new Claim("sub", Guid.NewGuid().ToString("D")));
        var method = typeof(ClassLevelAllowAllService).GetMethod(nameof(ClassLevelAllowAllService.Save))!;

        var sut = new RoleAccessAuthorizer();
        var allowed = sut.IsAuthorized(principal, method);

        Assert.IsTrue(allowed);
    }

    [TestMethod]
    public void IsAuthorized_MethodLevelAllowAll_OverridesClassRoleRequirement()
    {
        var principal = BuildPrincipal(new Claim("role", "viewer"));
        var method = typeof(MethodAllowAllOverrideService).GetMethod(nameof(MethodAllowAllOverrideService.Save))!;

        var sut = new RoleAccessAuthorizer();
        var allowed = sut.IsAuthorized(principal, method);

        Assert.IsTrue(allowed);
    }

    [TestMethod]
    public void IsAuthorized_MethodLevelRoleRequirement_OverridesClassAllowAll()
    {
        var principal = BuildPrincipal(new Claim("role", "viewer"));
        var method = typeof(MethodRoleOverridesClassAllowAllService).GetMethod(nameof(MethodRoleOverridesClassAllowAllService.Save))!;

        var sut = new RoleAccessAuthorizer();
        var allowed = sut.IsAuthorized(principal, method);

        Assert.IsFalse(allowed);
    }

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "unit-test");
        return new ClaimsPrincipal(identity);
    }

    [RoleAccess("owner")]
    private sealed class ClassLevelRoleService
    {
        public void Save() { }
    }

    [RoleAccess("owner")]
    private sealed class MethodOverrideRoleService
    {
        [RoleAccess("admin")]
        public void Save() { }
    }

    private sealed class RoleIdService
    {
        [RoleAccessId("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9")]
        public void Save() { }
    }

    [AllowAllRoleAccess]
    private sealed class ClassLevelAllowAllService
    {
        public void Save() { }
    }

    [RoleAccess("admin")]
    private sealed class MethodAllowAllOverrideService
    {
        [AllowAllRoleAccess]
        public void Save() { }
    }

    [AllowAllRoleAccess]
    private sealed class MethodRoleOverridesClassAllowAllService
    {
        [RoleAccess("admin")]
        public void Save() { }
    }
}
