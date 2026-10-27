using System.Security.Claims;
using IBeam.Identity.Models;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Services;
namespace IBeam.Tests.Management.Identity;

[TestClass]
public sealed class CustomerDelegationTests
{
    [TestMethod]
    public void ExistingAdministratorsGainNoImplicitCustomerAuthority()
    {
        var permissions = ManagementIdentityPermissions.ForRoles([ManagementIdentityRoles.Administrator]);
        Assert.IsFalse(permissions.Any(x => x.StartsWith("management.application-access.delegate.identity.", StringComparison.Ordinal)));
        Assert.IsFalse(permissions.Any(ManagementCustomerIdentityPermissionPolicy.IsAssignable));
    }
    [TestMethod]
    public void TrustedConfigurationBindsDelegationToNamedUserApplicationAndOperation()
    {
        var user = Guid.NewGuid(); var app = Guid.NewGuid();
        var options = Options(user, app);
        var permissions = ManagementIdentityPermissions.CustomerDelegationsFor(user, [ManagementIdentityRoles.Administrator], options);
        CollectionAssert.AreEqual(new[] { ManagementCustomerIdentityPermissionPolicy.DelegationFor(ManagementCustomerIdentityOperations.TenantsRead, app) }, permissions.ToArray());
        Assert.HasCount(0, ManagementIdentityPermissions.CustomerDelegationsFor(Guid.NewGuid(), [ManagementIdentityRoles.Administrator], options));
        Assert.HasCount(0, ManagementIdentityPermissions.CustomerDelegationsFor(user, [ManagementIdentityRoles.Support], options));
    }
    [TestMethod]
    public void RefreshRemovesStaleDelegationsAndAppliesCurrentHostCeiling()
    {
        var user = Guid.NewGuid(); var org = Guid.NewGuid(); var app = Guid.NewGuid();
        var options = Options(user, app); options.OrganizationId = org;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new("uid", user.ToString()), new(ManagementClaimTypes.OrganizationId, org.ToString()),
            new(ManagementClaimTypes.Permission, "forged-stale-authority") ], "Support"));
        var member = new ManagementLiveMember(new IdentityUser(user, null, false), new(org, "Support", [ManagementIdentityRoles.Administrator], true));
        ManagementIdentitySessionService.RefreshPrincipal(principal, member, options);
        Assert.IsFalse(principal.HasClaim(ManagementClaimTypes.Permission, "forged-stale-authority"));
        var ceiling = ManagementCustomerIdentityPermissionPolicy.DelegationFor(ManagementCustomerIdentityOperations.TenantsRead, app);
        Assert.IsTrue(principal.HasClaim(ManagementClaimTypes.Permission, ceiling));
        options.CustomerDelegations.Clear();
        ManagementIdentitySessionService.RefreshPrincipal(principal, member, options);
        Assert.IsFalse(principal.HasClaim(ManagementClaimTypes.Permission, ceiling));
        options = Options(user, app); options.OrganizationId = org;
        ManagementIdentitySessionService.RefreshPrincipal(principal,
            member with { Membership = member.Membership with { Roles = [ManagementIdentityRoles.Support] } }, options);
        Assert.IsFalse(principal.HasClaim(ManagementClaimTypes.Permission, ceiling));
    }
    [TestMethod]
    [DataRow("wildcard")] [DataRow("missing-user")] [DataRow("missing-app")] [DataRow("duplicate-binding")] [DataRow("duplicate-operation")] [DataRow("discovery")]
    public void InvalidOrUnboundedDelegationConfigurationFailsValidation(string change)
    {
        var options = Options(Guid.NewGuid(), Guid.NewGuid());
        var binding = options.CustomerDelegations[0];
        if (change == "wildcard") binding.Operations = ["*"];
        if (change == "discovery") binding.Operations = [ManagementCustomerIdentityOperations.Info];
        if (change == "missing-user") binding.UserId = Guid.Empty;
        if (change == "missing-app") binding.ApplicationId = Guid.Empty;
        if (change == "duplicate-binding") options.CustomerDelegations.Add(binding);
        if (change == "duplicate-operation") binding.Operations = [ManagementCustomerIdentityOperations.TenantsRead, ManagementCustomerIdentityOperations.TenantsRead];
        Assert.IsFalse(ManagementIdentityPermissions.ValidCustomerDelegations(options));
    }
    private static ManagementIdentityOptions Options(Guid user, Guid app) => new() { CustomerDelegations = [
        new() { UserId = user, ApplicationId = app, Operations = [ManagementCustomerIdentityOperations.TenantsRead] } ] };
}
