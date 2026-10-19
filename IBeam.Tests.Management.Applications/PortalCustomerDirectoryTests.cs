using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class PortalCustomerDirectoryTests
{
    [TestMethod]
    public void DirectoryDebugStringsDoNotContainCustomerIdentityOrSigningMaterial()
    {
        var canary = CustomerDirectoryBackend.Canary;
        var tenant = new ManagementCustomerTenantDto(Guid.NewGuid(), canary, "Active", null, null, "v1");
        Assert.DoesNotContain(canary, tenant.ToString());
        var connection = new ManagementCustomerIdentityConnection { Key = new(canary) };
        Assert.DoesNotContain(canary, connection.ToString());
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(connection));
    }
    private static string Path(Guid app, string suffix = "") => "/api/management/applications/" + app + "/tenants" + suffix;
    private static async Task<JsonDocument> Ok(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.DoesNotContain(CustomerDirectoryBackend.Canary, text); Assert.DoesNotContain(ConnectionFixture.Key, text);
        Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
        return JsonDocument.Parse(text);
    }
    [TestMethod]
    public async Task SelectedAppKeepsOverlappingTenantUserAndInviteIdsIsolatedAndDiscardsPrivateFields()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerDirectories: true, audit: true);
        foreach (var key in new[] { "qurvia", "bindry" }) {
            var app = await f.Directories.AddApp(f, key);
            foreach (var suffix in new[] { "", "/" + f.Directories.Tenant, "/" + f.Directories.Tenant + "/users",
                "/" + f.Directories.Tenant + "/users/" + f.Directories.User, "/" + f.Directories.Tenant + "/invites",
                "/" + f.Directories.Tenant + "/invites/" + f.Directories.Invite }) {
                using var response = await f.Get(Path(app, suffix)); using var json = await Ok(response);
                var data = json.RootElement.GetProperty("data");
                var row = data.ValueKind == JsonValueKind.Array ? data[0] : data;
                Assert.AreEqual(f.Directories.Tenant, row.GetProperty("tenantId").GetGuid());
                Assert.AreEqual(suffix.Contains("users") || suffix.Contains("invites") ? key + "@example.test" : key,
                    row.GetProperty(suffix.Contains("users") || suffix.Contains("invites") ? "email" : "name").GetString());
            }
        }
        var audit = JsonSerializer.Serialize(f.Audit.Rows);
        Assert.DoesNotContain("@example.test", audit); Assert.DoesNotContain(CustomerDirectoryBackend.Canary, audit);
        Assert.IsTrue(f.Audit.Rows.Any(x => x.Operation == "management.portal.customer-identity.users.read"));
        Assert.AreEqual(0, f.Data.Backend.Calls);
    }
    [TestMethod]
    public async Task EmptyPageRetainsOpaqueContinuationAndFixedExpiryWhileQueryAndAppReplayFails()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerDirectories: true);
        var app = await f.Directories.AddApp(f); var other = await f.Directories.AddApp(f, "bindry");
        f.Directories.Empty = true;
        using var response = await f.Get(Path(app) + "?pageSize=1"); using var json = await Ok(response);
        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetArrayLength());
        var cursor = json.RootElement.GetProperty("continuationToken").GetString()!;
        Assert.DoesNotContain("provider-next", cursor);
        using var next = await f.Get(Path(app) + "?pageSize=1&continuationToken=" + cursor); using var page = await Ok(next);
        Assert.AreEqual(JsonValueKind.Null, page.RootElement.GetProperty("continuationToken").ValueKind);
        var calls = f.Directories.Calls;
        foreach (var path in new[] { Path(other) + "?pageSize=1", Path(app) + "?pageSize=2", Path(app) + "?pageSize=1&status=Active",
            Path(app) + "?pageSize=1&namePrefix=x", Path(app, "/" + f.Directories.Tenant + "/users") + "?pageSize=1" }) {
            using var invalid = await f.Get(path + "&continuationToken=" + cursor);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode); }
        Assert.AreEqual(calls, f.Directories.Calls);
        f.Data.Clock.Now = f.Data.Clock.Now.AddMinutes(16);
        using var expired = await f.Get(Path(app) + "?pageSize=1&continuationToken=" + cursor);
        Assert.AreEqual(HttpStatusCode.BadRequest, expired.StatusCode);
    }
    [TestMethod]
    public async Task DeniedPrincipalsAndMissingHumanMachineOrPolicyGrantsNeverResolveSecretsOrCallTargets()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerDirectories: true);
        var app = await f.Directories.AddApp(f, human: [ManagementCustomerIdentityOperations.TenantsRead]);
        foreach (var token in new[] { "other", "foreign", "admin", "machine" }) {
            using var response = await f.Get(Path(app), token); Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode); }
        using var customer = await f.Get(Path(app), "customer"); Assert.AreEqual(HttpStatusCode.Unauthorized, customer.StatusCode);
        using var users = await f.Get(Path(app, "/" + f.Directories.Tenant + "/users")); Assert.AreEqual(HttpStatusCode.Forbidden, users.StatusCode);
        var old = f.Data.Credentials.Rows[0];
        f.Data.Credentials.Rows[0] = old with { Capabilities = [ManagementCustomerIdentityOperations.Info] };
        using var machine = await f.Get(Path(app)); Assert.AreEqual(HttpStatusCode.Forbidden, machine.StatusCode);
        f.Data.Credentials.Rows[0] = old with { CustomerIdentity = old.CustomerIdentity! with { Info = old.CustomerIdentity.Info! with { Capabilities = [ManagementCustomerIdentityOperations.Info] } } };
        using var policy = await f.Get(Path(app)); Assert.AreEqual(HttpStatusCode.Forbidden, policy.StatusCode);
        Assert.AreEqual(0, f.Data.Secrets.Reads); Assert.AreEqual(0, f.Directories.Calls);
    }
    [TestMethod]
    [DataRow("malformed", 502)] [DataRow("oversize", 502)] [DataRow("realm", 502)] [DataRow("page", 502)]
    [DataRow("scope", 502)] [DataRow("302", 502)] [DataRow("400", 400)] [DataRow("401", 403)]
    [DataRow("403", 403)] [DataRow("404", 404)] [DataRow("429", 429)] [DataRow("500", 503)]
    public async Task InvalidAndProviderErrorResponsesProduceBoundedSafeFailures(string failure, int status)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerDirectories: true);
        var app = await f.Directories.AddApp(f); f.Directories.Failure = failure;
        using var response = await f.Get(Path(app, "/" + f.Directories.Tenant + "/users"));
        Assert.AreEqual(status, (int)response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(CustomerDirectoryBackend.Canary, await response.Content.ReadAsStringAsync());
    }
    [TestMethod]
    [DataRow("revoke")] [DataRow("disable")] [DataRow("connection")] [DataRow("timeout")]
    public async Task ChangesDuringReadAreRecheckedBeforeReturningAnyCustomerData(string change)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerDirectories: true);
        var app = await f.Directories.AddApp(f);
        f.Directories.DuringData = async ct => {
            if (change == "timeout") { await Task.Delay(Timeout.Infinite, ct); return; }
            if (change == "revoke") {
                var k = (f.Data.Access.Org, app, f.Data.Access.Reader);
                f.Data.Access.Access.Rows[k] = f.Data.Access.Access.Rows[k] with { RevokedUtc = DateTimeOffset.UtcNow };
            } else {
                var k = (f.Data.Access.Org, app); var old = f.Data.Access.Apps.Rows[k];
                f.Data.Access.Apps.Rows[k] = change == "disable" ? old with { IsEnabled = false } : old with { Version = "rotated" };
            }
        };
        using var response = await f.Get(Path(app));
        Assert.AreEqual(change == "timeout" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("qurvia", await response.Content.ReadAsStringAsync());
    }
    [TestMethod]
    public async Task CursorCannotMoveBetweenSupportUsersOrganizationsRealmsOrConnectionVersions()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerDirectories: true);
        var app = await f.Directories.AddApp(f);
        using var first = await f.Get(Path(app)); using var json = await Ok(first);
        var cursor = json.RootElement.GetProperty("continuationToken").GetString();
        var path = Path(app) + "?continuationToken=" + cursor;
        var original = f.Data.Access.Access.Rows[(f.Data.Access.Org, app, f.Data.Access.Reader)];
        f.Data.Access.Access.Rows[(f.Data.Access.Org, app, f.OtherUser)] = original with { UserId = f.OtherUser };
        using var other = await f.Get(path, "other"); Assert.AreEqual(HttpStatusCode.BadRequest, other.StatusCode);
        using var foreign = await f.Get(path, "foreign"); Assert.AreEqual(HttpStatusCode.Forbidden, foreign.StatusCode);
        var credential = f.Data.Credentials.Rows[0];
        f.Data.Credentials.Rows[0] = credential with { CustomerIdentity = credential.CustomerIdentity! with { Info = credential.CustomerIdentity.Info! with { IdentityRealmId = Guid.NewGuid() } } };
        using var realm = await f.Get(path); Assert.AreEqual(HttpStatusCode.BadRequest, realm.StatusCode);
        f.Data.Credentials.Rows[0] = credential;
        var k = (f.Data.Access.Org, app); f.Data.Access.Apps.Rows[k] = f.Data.Access.Apps.Rows[k] with { Version = "changed" };
        using var connection = await f.Get(path); Assert.AreEqual(HttpStatusCode.BadRequest, connection.StatusCode);
    }
    [TestMethod]
    public async Task OptInAndDirectServiceCallsUseTheSameLiveAuthorizationBoundary()
    {
        await using var off = await PortalHttpFixture.CreateAsync(install: false);
        using var absent = await off.Get(Path(Guid.NewGuid())); Assert.AreEqual(HttpStatusCode.NotFound, absent.StatusCode);
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerDirectories: true);
        var app = await f.Directories.AddApp(f);
        using var scope = f.App.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<IManagementPortalCustomerDirectory<ManagementCustomerTenantDto>>();
        // No trusted HTTP management principal in this service scope.
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => directory.ListAsync(app, null, new()));
        Assert.AreEqual(0, f.Data.Secrets.Reads); Assert.AreEqual(0, f.Directories.Calls);
        using var invalid = await f.Get(Path(app) + "?pageSize=invalid"); Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(0, f.Data.Secrets.Reads);
    }
}
