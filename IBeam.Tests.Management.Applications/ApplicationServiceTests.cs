using System.Security.Claims;
using IBeam.Identity.Models;
using IBeam.Management;
using IBeam.Management.Identity.Services;

namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class ApplicationServiceTests
{
    [TestMethod]
    public async Task OrganizationScopeIsMandatoryAndRegistrationGrantsNoDiagnosticOrCustomerAccess()
    {
        using var f = new ApplicationFixture();
        var app = await f.Service.CreateAsync(ApplicationFixture.Request());
        Assert.IsFalse(app.HasConnection);
        Assert.HasCount(1, f.Repository.Rows);
        f.OrganizationId = Guid.NewGuid();
        Assert.HasCount(0, (await f.Service.ListAsync()).Items);
        await Assert.ThrowsAsync<ManagementApplicationNotFoundException>(() => f.Service.GetAsync(app.ApplicationId));
        await Assert.ThrowsAsync<ManagementApplicationNotFoundException>(() => f.Service.UpdateAsync(app.ApplicationId, new()
            { Name = "Other", BaseUrl = app.BaseUrl, ExpectedVersion = app.Version }));
        await f.Service.CreateAsync(ApplicationFixture.Request());
        Assert.HasCount(2, f.Repository.Rows);
        Assert.IsTrue(f.Audit.All(x => x.BeforeJson is null && x.AfterJson is null));
    }
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public async Task ReadersAndMachinesCannotAdminister(bool admin, bool machine)
    {
        using var f = new ApplicationFixture { Admin = admin, Machine = machine };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.ListAsync());
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.CreateAsync(ApplicationFixture.Request()));
        Assert.AreEqual(0, f.Repository.Reads);
        Assert.IsTrue(f.Audit.All(x => !x.Succeeded));
        Assert.HasCount(2, f.Audit);
    }
    [TestMethod]
    public async Task DuplicateAndStaleUpdatesConflictAndChangingUrlDetachesConnection()
    {
        using var f = new ApplicationFixture();
        var app = await f.Service.CreateAsync(ApplicationFixture.Request());
        await Assert.ThrowsAsync<ManagementApplicationConflictException>(() => f.Service.CreateAsync(ApplicationFixture.Request()));
        var key = (f.OrganizationId, app.ApplicationId);
        f.Repository.Rows[key] = f.Repository.Rows[key] with { ActiveCredentialId = Guid.NewGuid() };
        var updated = await f.Service.UpdateAsync(app.ApplicationId, new()
            { Name = "Renamed", BaseUrl = "https://new.qurvia.app", IsEnabled = false, ExpectedVersion = app.Version });
        Assert.IsFalse(updated.IsEnabled); Assert.IsFalse(updated.HasConnection);
        await Assert.ThrowsAsync<ManagementApplicationConflictException>(() => f.Service.UpdateAsync(app.ApplicationId, new()
            { Name = app.Name, BaseUrl = app.BaseUrl, ExpectedVersion = app.Version }));
    }
    [TestMethod]
    [DataRow("http://api.qurvia.app")]
    [DataRow("https://127.0.0.1")]
    [DataRow("https://api.qurvia.app/path")]
    [DataRow("https://user:pass@api.qurvia.app")]
    [DataRow("https://api.qurvia.app?key=secret")]
    [DataRow("https://api.qurvia.app:444")]
    public async Task UnsafeOriginsAreRejectedWithoutStorageWrites(string url)
    {
        using var f = new ApplicationFixture();
        var request = ApplicationFixture.Request(); request.BaseUrl = url;
        await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => f.Service.CreateAsync(request));
        Assert.HasCount(0, f.Repository.Rows);
    }
    [TestMethod]
    public async Task PaginationBoundsCancellationAndProviderErrorsAreSafe()
    {
        using var f = new ApplicationFixture();
        var app = await f.Service.CreateAsync(ApplicationFixture.Request());
        await f.Service.CreateAsync(ApplicationFixture.Request("bindry"));
        var first = await f.Service.ListAsync(1);
        var second = await f.Service.ListAsync(1, first.ContinuationToken);
        Assert.AreNotEqual(first.Items[0].ApplicationId, second.Items[0].ApplicationId);
        await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => f.Service.ListAsync(101));
        await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => f.Service.ListAsync(1, "not-a-cursor"));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.GetAsync(app.ApplicationId, cancel.Token));
        f.Repository.Outage = true;
        var error = await Assert.ThrowsAsync<ManagementApplicationUnavailableException>(() => f.Service.GetAsync(app.ApplicationId));
        Assert.DoesNotContain("secret", error.Message);
        Assert.DoesNotContain("unsafe", System.Text.Json.JsonSerializer.Serialize(f.Audit));
    }
    [TestMethod]
    public void LiveManagementRolesRebuildPermissionsWithoutCustomerRoleEscalation()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ManagementClaimTypes.Permission, ManagementApplicationOperations.Admin)], "Support"));
        var member = new ManagementLiveMember(new IdentityUser(Guid.NewGuid(), null, true, null, true, false, null), new TenantInfo(Guid.NewGuid(), "Support", [ManagementIdentityRoles.Support], true));
        ManagementIdentitySessionService.RefreshPrincipal(principal, member);
        Assert.IsFalse(principal.HasClaim(ManagementClaimTypes.Permission, ManagementApplicationOperations.Admin));
        ManagementIdentitySessionService.RefreshPrincipal(principal, member with { Membership = member.Membership with { Roles = [ManagementIdentityRoles.Administrator] } });
        Assert.IsTrue(principal.HasClaim(ManagementClaimTypes.Permission, ManagementApplicationOperations.Admin));
        Assert.IsFalse(ManagementIdentityPermissions.ForRoles(["Owner", "Admin"]).Contains(ManagementApplicationOperations.Admin));
    }
}
