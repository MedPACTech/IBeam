using IBeam.Identity.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class RoleAuthorizationAttributeTests
{
    private static readonly Guid AdminRoleId = Guid.Parse("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9");

    [TestMethod]
    public void AllowRolesAttribute_SetsAuthorizeRolesCsv()
    {
        var attr = new AllowRolesAttribute("owner", "admin");

        Assert.AreEqual("owner,admin", attr.Roles);
    }

    [TestMethod]
    public void AllowRoleIdsAttribute_SetsDynamicPolicyName()
    {
        var attr = new AllowRoleIdsAttribute(AdminRoleId.ToString("D"));

        Assert.AreEqual(
            $"{RoleIdsAuthorizationPolicyProvider.PolicyPrefix}{AdminRoleId:D}",
            attr.Policy);
    }

    [TestMethod]
    public async Task RoleIdsPolicyProvider_BuildsPolicyWithRequirement()
    {
        var provider = new RoleIdsAuthorizationPolicyProvider(
            Options.Create(new AuthorizationOptions()));

        var policy = await provider.GetPolicyAsync(
            $"{RoleIdsAuthorizationPolicyProvider.PolicyPrefix}{AdminRoleId:D}");

        Assert.IsNotNull(policy);
        Assert.IsTrue(policy.Requirements.OfType<RequireRoleIdsRequirement>().Any());
    }

    [TestMethod]
    public async Task RequireRoleIdsAuthorizationHandler_Succeeds_WhenUserHasMatchingRidClaim()
    {
        var requirement = new RequireRoleIdsRequirement(new List<Guid> { AdminRoleId });
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("rid", AdminRoleId.ToString("D")) },
                "unit-test"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);

        var sut = new RequireRoleIdsAuthorizationHandler();
        await sut.HandleAsync(context);

        Assert.IsTrue(context.HasSucceeded);
    }
}
