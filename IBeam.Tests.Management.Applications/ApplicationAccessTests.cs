using System.Text.Json;
using IBeam.Management;
namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class ApplicationAccessTests
{
    [TestMethod]
    public async Task FullCustomerManagementCoexistsWithBothDiagnosticPermissionsAndStillRejectsUnknownAuthority()
    {
        using var f = new AccessFixture(); var app = await f.CreateApp();
        var customer = IBeam.Management.CustomerIdentity.ManagementCustomerIdentityOperations.All
            .Where(IBeam.Management.CustomerIdentity.ManagementCustomerIdentityPermissionPolicy.IsAssignable).ToArray();
        foreach(var operation in customer) f.CustomerDelegations.Add((app, operation));
        var permissions = customer.Concat(new[] { ManagementOperations.ErrorsRead, ManagementOperations.LogsRead }).ToArray();
        Assert.HasCount(18, permissions); // Includes the two independent ownership operations.
        var grant = await f.Service.PutAsync(app, f.Reader, new() { Permissions = permissions });
        f.Actor = f.Reader; f.CanAdminister = false;
        foreach(var permission in permissions) await f.Service.RequireAsync(app, permission);
        f.Actor = f.Admin; f.CanAdminister = true;
        await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => f.Service.PutAsync(app, f.Reader,
            new() { Permissions = permissions.Append("management.identity.*").ToArray(), ExpectedVersion = grant.Version }));
        f.Actor = f.Reader; f.CanAdminister = false;
        foreach(var permission in permissions) await f.Service.RequireAsync(app, permission);
    }
    [TestMethod]
    public async Task IndependentPermissionsManyAppsAndRevocationAreLiveWithoutPrincipalRefresh()
    {
        using var f = new AccessFixture(); var app = await f.CreateApp(); var other = await f.CreateApp("bindry");
        var first = await f.Service.PutAsync(app, f.Reader, new() { Permissions = [ManagementOperations.ErrorsRead] });
        await f.Service.PutAsync(other, f.Reader, new() { Permissions = [ManagementOperations.LogsRead] });
        f.Actor = f.Reader; f.CanAdminister = false;
        await f.Service.RequireAsync(app, ManagementOperations.ErrorsRead);
        await f.Service.RequireAsync(other, ManagementOperations.LogsRead);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(app, ManagementOperations.LogsRead));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(other, ManagementOperations.ErrorsRead));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.PutAsync(app, f.Reader,
            new() { Permissions = [ManagementOperations.LogsRead], ExpectedVersion = first.Version }));
        f.Actor = f.Admin; f.CanAdminister = true;
        await f.Service.RevokeAsync(app, f.Reader, first.Version);
        var final = f.Access.Rows[(f.Org, app, f.Reader)];
        Assert.IsNotNull(final.RevokedUtc); Assert.AreEqual(f.Admin, final.RevokedBy);
        f.Actor = f.Reader; f.CanAdminister = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(app, ManagementOperations.ErrorsRead));
        await f.Service.RequireAsync(other, ManagementOperations.LogsRead);
        Assert.IsTrue(f.Audit.Any(x => x.Action == "management.application-access.revoke" && x.Succeeded));
        Assert.IsTrue(f.Audit.All(x => x.BeforeJson is null && x.AfterJson is null));
    }
    [TestMethod]
    [DataRow("user")] [DataRow("organization")] [DataRow("application")] [DataRow("missing-app")] [DataRow("missing-assignment")] [DataRow("foreign-row")]
    public async Task MissingDisabledAndForeignStateDeniesEvenWithStalePermissionClaims(string change)
    {
        using var f = new AccessFixture(); var app = await f.CreateApp();
        await f.Service.PutAsync(app, f.Reader, new() { Permissions = [ManagementOperations.ErrorsRead] });
        f.Actor = f.Reader; f.CanAdminister = false;
        if (change is "user" or "organization") f.Users[(f.Org, f.Reader)] = f.Users[(f.Org, f.Reader)] with { IsEnabled = false };
        if (change == "application") f.Apps.Rows[(f.Org, app)] = f.Apps.Rows[(f.Org, app)] with { IsEnabled = false };
        if (change == "missing-app") f.Apps.Rows.Clear();
        if (change == "missing-assignment") f.Access.Rows.Clear();
        if (change == "foreign-row") f.Access.Rows[(f.Org, app, f.Reader)] = f.Access.Rows[(f.Org, app, f.Reader)] with { OrganizationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(app, ManagementOperations.ErrorsRead));
    }
    [TestMethod]
    public async Task AdministratorsNeedAssignmentsAndAnExplicitDelegationCeiling()
    {
        using var f = new AccessFixture(); var app = await f.CreateApp();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(app, ManagementOperations.ErrorsRead));
        f.DelegateLogs = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.PutAsync(app, f.Reader, new() { Permissions = [ManagementOperations.LogsRead] }));
        await f.Service.PutAsync(app, f.Reader, new() { Permissions = [ManagementOperations.ErrorsRead] });
        f.CanAdminister = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.ListAsync(app));
        f.CanAdminister = true; f.Users[(f.Org, f.Admin)] = f.Users[(f.Org, f.Admin)] with { IsAdministrator = false };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.ListAsync(app));
    }
    [TestMethod]
    public async Task ValidationScopeDuplicatesConcurrencyAndSafeOutages()
    {
        using var f = new AccessFixture(); var app = await f.CreateApp();
        foreach (var permission in new[] { "*", "Owner", ManagementOperations.IdentityRead })
            await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => f.Service.PutAsync(app, f.Reader, new() { Permissions = [permission] }));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.PutAsync(app, Guid.NewGuid(), new() { Permissions = [ManagementOperations.ErrorsRead] }));
        var first = await f.Service.PutAsync(app, f.Reader, new() { Permissions = [ManagementOperations.ErrorsRead] });
        await Assert.ThrowsAsync<ManagementApplicationConflictException>(() => f.Service.PutAsync(app, f.Reader, new() { Permissions = [ManagementOperations.LogsRead] }));
        await Assert.ThrowsAsync<ManagementApplicationConflictException>(() => f.Service.RevokeAsync(app, f.Reader, "stale"));
        await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => f.Service.RevokeAsync(app, f.Reader, "*"));
        await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => f.Service.ListAsync(app, 101));
        await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => f.Service.ListAsync(app, 1, "foreign-storage-cursor"));
        await f.Service.PutAsync(app, f.Admin, new() { Permissions = [ManagementOperations.LogsRead] });
        var page = await f.Service.ListAsync(app, 1);
        Assert.HasCount(1, page.Items); Assert.IsNotNull(page.ContinuationToken);
        Assert.AreNotEqual(page.Items[0].UserId, (await f.Service.ListAsync(app, 1, page.ContinuationToken)).Items[0].UserId);
        f.Access.Outage = true;
        await Assert.ThrowsAsync<ManagementApplicationUnavailableException>(() => f.Service.PutAsync(app, f.Reader, new() { Permissions = [ManagementOperations.ErrorsRead], ExpectedVersion = first.Version }));
        Assert.DoesNotContain("provider-secret-canary", JsonSerializer.Serialize(f.Audit));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.ListAsync(app, ct: cancel.Token));
    }
}
