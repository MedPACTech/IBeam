using IBeam.Management;
using IBeam.Management.Identity.Services;
using IBeam.Management.Operations;

namespace IBeam.Tests.Management.Identity;

[TestClass]
public sealed class RegisteredOperationDelegationTests
{
    public sealed record Request(Guid TenantId);
    public sealed record Result(string Status);
    private const string Operation = "management.apps.fixture.inspect";
    [TestMethod]
    public void ExtensionDelegationIsExplicitPerHumanAndAppAndNeverImpliedByAdministrator()
    {
        var registry = new ManagementOperationRegistry([new(Operation, "GET", "/api/management-operations/fixture/{tenantId:guid}",
            new(typeof(Request), "fixture.inspect.request"), new(typeof(Result), "fixture.inspect.result"),
            typeof(IManagementOperationHandler<Request, Result>), [Operation], 0, Operation)]);
        var user = Guid.NewGuid(); var app = Guid.NewGuid();
        var options = new ManagementIdentityOptions();
        Assert.HasCount(0, ManagementIdentityPermissions.CustomerDelegationsFor(user, [ManagementIdentityRoles.Administrator], options, registry));
        options.CustomerDelegations.Add(new() { UserId = user, ApplicationId = app, Operations = [Operation] });
        Assert.IsFalse(ManagementIdentityPermissions.ValidCustomerDelegations(options));
        Assert.IsTrue(ManagementIdentityPermissions.ValidCustomerDelegations(options, registry));
        CollectionAssert.AreEqual(new[] { registry.DelegationFor(Operation, app) },
            ManagementIdentityPermissions.CustomerDelegationsFor(user, [ManagementIdentityRoles.Administrator], options, registry).ToArray());
        Assert.HasCount(0, ManagementIdentityPermissions.CustomerDelegationsFor(Guid.NewGuid(), [ManagementIdentityRoles.Administrator], options, registry));
        Assert.HasCount(0, ManagementIdentityPermissions.CustomerDelegationsFor(user, [ManagementIdentityRoles.Support], options, registry));
        Assert.DoesNotContain(Operation, ManagementIdentityPermissions.ForRoles([ManagementIdentityRoles.Administrator]));
        options.CustomerDelegations[0].Operations = [Operation, "management.apps.fixture.*"];
        Assert.IsFalse(ManagementIdentityPermissions.ValidCustomerDelegations(options, registry));
    }
}
