using System.Security.Claims;
using IBeam.Identity.Authorization;

namespace IBeam.Tests.Identity;

[TestClass]
public sealed class ClaimsPrincipalRoleExtensionsTests
{
    private static readonly Guid AdminRoleId = Guid.Parse("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9");

    [TestMethod]
    public void HasRole_MatchesRoleClaimCaseInsensitive()
    {
        var principal = BuildPrincipal(new Claim("role", "Admin"));

        Assert.IsTrue(principal.HasRole("admin"));
        Assert.IsFalse(principal.HasRole("owner"));
    }

    [TestMethod]
    public void HasAnyRole_MatchesAny()
    {
        var principal = BuildPrincipal(new Claim(ClaimTypes.Role, "owner"));

        Assert.IsTrue(principal.HasAnyRole("viewer", "owner"));
        Assert.IsFalse(principal.HasAnyRole("viewer", "guest"));
    }

    [TestMethod]
    public void HasRoleId_And_HasAnyRoleId_MatchRidClaims()
    {
        var principal = BuildPrincipal(new Claim("rid", AdminRoleId.ToString("D")));

        Assert.IsTrue(principal.HasRoleId(AdminRoleId));
        Assert.IsTrue(principal.HasAnyRoleId(Guid.NewGuid(), AdminRoleId));
        Assert.IsFalse(principal.HasRoleId(Guid.NewGuid()));
    }

    [TestMethod]
    public void Extensions_ReturnFalse_ForUnauthenticatedPrincipal()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.IsFalse(principal.HasRole("admin"));
        Assert.IsFalse(principal.HasAnyRole("admin"));
        Assert.IsFalse(principal.HasRoleId(AdminRoleId));
        Assert.IsFalse(principal.HasAnyRoleId(AdminRoleId));
    }

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "unit-test"));
}
