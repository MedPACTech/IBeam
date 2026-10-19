using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services;
using IBeam.Management.Services.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class CustomerIdentityPermissionTests
{
    public static IEnumerable<object[]> CustomerOperations => ManagementCustomerIdentityOperations.All
        .Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable).Select(x => new object[] { x });
    [TestMethod, DynamicData(nameof(CustomerOperations))]
    public async Task ExplicitOperationGrantsFollowTheCompositeMatrixWithoutImplicitPrivileges(string operation)
    {
        using var f = new AccessFixture();
        var app = await f.CreateApp();
        var required = ManagementCustomerIdentityPermissionPolicy.RequiredFor(operation).ToArray();
        foreach (var permission in required) f.CustomerDelegations.Add((app, permission));
        await f.Service.PutAsync(app, f.Reader, new() { Permissions = required });
        f.Actor = f.Reader; f.CanAdminister = false;
        await f.Service.RequireAsync(app, operation);
        foreach (var other in ManagementCustomerIdentityOperations.All.Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable))
        {
            if (ManagementCustomerIdentityPermissionPolicy.RequiredFor(other).All(required.Contains)) await f.Service.RequireAsync(app, other);
            else await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(app, other));
        }
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(app, ManagementOperations.ErrorsRead));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(app, ManagementOperations.LogsRead));
    }
    [TestMethod]
    public async Task CustomerDelegationIsExplicitAndBoundToApplicationAndOperationIncludingSelfAssignment()
    {
        using var f = new AccessFixture();
        var app = await f.CreateApp(); var other = await f.CreateApp("bindry");
        var read = ManagementCustomerIdentityOperations.TenantsRead;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.PutAsync(app, f.Admin, new() { Permissions = [read] }));
        f.CustomerDelegations.Add((other, read));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.PutAsync(app, f.Reader, new() { Permissions = [read] }));
        f.CustomerDelegations.Add((app, read));
        var grant = await f.Service.PutAsync(app, f.Reader, new() { Permissions = [read] });
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.PutAsync(app, f.Reader,
            new() { Permissions = [ManagementCustomerIdentityOperations.UsersRead], ExpectedVersion = grant.Version }));
        f.CustomerDelegations.Clear();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RevokeAsync(app, f.Reader, grant.Version));
        Assert.IsNull(f.Access.Rows[(f.Org, app, f.Reader)].RevokedUtc);
    }
    [TestMethod]
    [DataRow("onboarding")] [DataRow("provisioning")] [DataRow("retry")]
    public async Task HighLevelPermissionAloneCannotExecuteItsUnderlyingWrites(string action)
    {
        using var f = new AccessFixture();
        var app = await f.CreateApp();
        var operation = action switch { "onboarding" => ManagementCustomerIdentityOperations.OnboardingCreate,
            "retry" => ManagementCustomerIdentityOperations.OnboardingRetry, _ => ManagementCustomerIdentityOperations.UsersProvision };
        f.CustomerDelegations.Add((app, operation));
        await f.Service.PutAsync(app, f.Reader, new() { Permissions = [operation] });
        f.Actor = f.Reader; f.CanAdminister = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(app, operation));
    }
    [TestMethod]
    public async Task LegacyAssignmentsKeepOnlyTheirOriginalDiagnosticAuthority()
    {
        using var f = new AccessFixture(); var app = await f.CreateApp();
        await f.Service.PutAsync(app, f.Reader, new() { Permissions = [ManagementOperations.ErrorsRead, ManagementOperations.LogsRead] });
        f.Actor = f.Reader; f.CanAdminister = false;
        foreach (var operation in ManagementCustomerIdentityOperations.All.Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable))
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RequireAsync(app, operation));
        await f.Service.RequireAsync(app, ManagementOperations.ErrorsRead);
        await f.Service.RequireAsync(app, ManagementOperations.LogsRead);
    }
    [TestMethod]
    public async Task IdentityOnlyAssignmentsAppearInSelectorAndExposeOnlyEffectiveOperations()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, selector: true, customerIdentity: true);
        var id = await f.Data.AddApp();
        var read = ManagementCustomerIdentityOperations.TenantsRead;
        var write = ManagementCustomerIdentityOperations.TenantsCreate;
        await Configure(f.Data, id, [read], [read, write], [read, write]);
        using var response = await f.Get("/api/management/applications");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var selected = json.RootElement.GetProperty("data");
        Assert.AreEqual(id, selected[0].GetProperty("applicationId").GetGuid());
        CollectionAssert.AreEqual(new[] { read }, selected[0].GetProperty("permissions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        using var customer = await f.Get("/api/management/applications/" + id + "/customer-management");
        using var metadata = JsonDocument.Parse(await customer.Content.ReadAsStringAsync());
        var effective = metadata.RootElement.GetProperty("data").GetProperty("info").GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Contains(read, effective); Assert.DoesNotContain(write, effective);
        f.Data.Access.Access.Rows[(f.Data.Access.Org, id, f.Data.Access.Reader)] =
            f.Data.Access.Access.Rows[(f.Data.Access.Org, id, f.Data.Access.Reader)] with { RevokedUtc = DateTimeOffset.UtcNow };
        using var revoked = await f.Get("/api/management/applications/" + id + "/customer-management");
        Assert.AreEqual(HttpStatusCode.Forbidden, revoked.StatusCode);
    }
    [TestMethod]
    [DataRow("machine")] [DataRow("installed-policy")] [DataRow("human")]
    public async Task MissingAnyCompositeGrantRemovesTheEffectiveOnboardingCapability(string missing)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, selector: true, customerIdentity: true);
        var id = await f.Data.AddApp();
        var operation = ManagementCustomerIdentityOperations.OnboardingCreate;
        var all = ManagementCustomerIdentityPermissionPolicy.RequiredFor(operation).ToArray();
        var narrowed = all.Where(x => x != ManagementCustomerIdentityOperations.RolesAssign).ToArray();
        await Configure(f.Data, id, missing == "human" ? narrowed : all, missing == "machine" ? narrowed : all, missing == "installed-policy" ? narrowed : all);
        using var response = await f.Get("/api/management/applications/" + id + "/customer-management");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var capabilities = json.RootElement.GetProperty("data").GetProperty("info").GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.DoesNotContain(operation, capabilities);
    }
    [TestMethod]
    public async Task DirectCustomerAuthorizationRechecksLiveConnectionAndActorState()
    {
        using var f = new PortalFixture();
        var id = await f.AddApp();
        var read = ManagementCustomerIdentityOperations.TenantsRead;
        await Configure(f, id, [read], [read], [read]);
        var operations = f.Access.Provider.GetRequiredService<IServiceOperationExecutor>();
        var availability = new ManagementCustomerIdentityAvailabilityService(f.Access, new ManagementApplicationStateProvider(f.Access.Apps),
            f.Credentials, f.Access.Service, operations);
        var auth = new ManagementCustomerIdentityAuthorizationService(f.Access.Service, availability, operations);
        await auth.RequireAsync(id, read);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => auth.RequireAsync(id, ManagementCustomerIdentityOperations.UsersProvision));
        var app = f.Access.Apps.Rows[(f.Access.Org, id)];
        f.Access.Apps.Rows[(f.Access.Org, id)] = app with { ActiveCredentialId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => auth.RequireAsync(id, read));
        f.Access.Apps.Rows[(f.Access.Org, id)] = app;
        f.Access.Users[(f.Access.Org, f.Access.Reader)] = new(f.Access.Org, f.Access.Reader, false, false);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => auth.RequireAsync(id, read));
        Assert.AreEqual(0, f.Secrets.Reads); Assert.AreEqual(0, f.Backend.Calls);
    }
    private static async Task Configure(PortalFixture f, Guid id, string[] human, string[] machine, string[] installed)
    {
        f.Access.Actor = f.Access.Admin; f.Access.CanAdminister = true;
        foreach (var operation in human) f.Access.CustomerDelegations.Add((id, operation));
        var existing = f.Access.Access.Rows[(f.Access.Org, id, f.Access.Reader)];
        await f.Access.Service.PutAsync(id, f.Access.Reader, new() { Permissions = human, ExpectedVersion = existing.Version });
        f.Access.Actor = f.Access.Reader; f.Access.CanAdminister = false;
        var row = f.Credentials.Rows.Single(x => x.ApplicationId == id);
        var position = f.Credentials.Rows.IndexOf(row);
        f.Credentials.Rows[position] = row with { Capabilities = new[] { ManagementCustomerIdentityOperations.Info }.Concat(machine).ToArray(),
            CustomerIdentity = new("available", new("qurvia", "Production", Guid.NewGuid(), "dedicated-app", 1,
                new[] { ManagementCustomerIdentityOperations.Info }.Concat(installed).ToArray())) };
    }
}
