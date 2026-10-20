using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class PortalTenantStatusTests
{
    private static Task<Guid> App(PortalHttpFixture f, string name = "qurvia", bool grant = true) => f.Directories.AddApp(f, name,
        grant ? [ManagementCustomerIdentityOperations.TenantsStatus] : [ManagementCustomerIdentityOperations.TenantsRead], includeTenantStatus: true);
    private static async Task<HttpResponseMessage> Write(PortalHttpFixture f, Guid app, bool active = false, string version = "tenant-v1", string key = "portal-tenant-status", object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/management/applications/{app}/tenants/{f.Directories.Tenant}/" + (active ? "activate" : "deactivate"));
        request.Headers.Add("Authorization", "Bearer reader"); request.Headers.Add("Idempotency-Key", key); request.Headers.TryAddWithoutValidation("If-Match", version);
        request.Content = JsonContent.Create(body ?? new { }); return await f.Client.SendAsync(request);
    }
    private static async Task<ManagementPortalTenantMutationResult> Read(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(status, response.StatusCode, text); Assert.DoesNotContain(CustomerDirectoryBackend.Canary, text);
        using var json = JsonDocument.Parse(text); return json.RootElement.GetProperty("data").Deserialize<ManagementPortalTenantMutationResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    [TestMethod]
    public async Task StatusIsAnIndependentLiveGrantAndSelectedAppsHaveSeparateVersions()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerTenantStatus: true, audit: true); var app = await App(f); var other = await App(f, "bindry");
        using var first = await Write(f, app); var disabled = await Read(first); Assert.AreEqual("Disabled", disabled.Tenant!.Status);
        using var replay = await Write(f, app); await Read(replay); Assert.AreEqual(1, f.Directories.TenantStatusWrites);
        using var stale = await Write(f, app, true, key: "portal-status-stale"); Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var enable = await Write(f, app, true, disabled.Tenant.Version!, "portal-status-enable"); var active = await Read(enable); Assert.AreEqual("Active", active.Tenant!.Status);
        using var separate = await Write(f, other); await Read(separate); Assert.AreEqual(3, f.Directories.TenantStatusWrites);
        var reader = await App(f, "medinsights", false); using var denied = await Write(f, reader); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using var invalid = await Write(f, app, true, active.Tenant.Version!, "portal-status-extra", new { delete = true }); Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.IsTrue(f.Audit.Rows.Any(x => x.Operation.EndsWith("tenants.status.succeeded", StringComparison.Ordinal) && x.ApplicationId == app));
    }
    [TestMethod]
    [DataRow("after-commit-timeout")] [DataRow("after-commit-malformed")] [DataRow("after-commit-503")]
    public async Task LostStatusReplyReconcilesReadOnlyAndCannotUseAnotherAppsReference(string failure)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerTenantStatus: true); var app = await App(f); f.Directories.Failure = failure;
        using var response = await Write(f, app); var result = await Read(response, HttpStatusCode.Accepted); Assert.IsNull(result.Tenant); Assert.AreEqual("unknown", result.Tracking.Status);
        f.Directories.Failure = null; using var poll = await f.Get(response.Headers.Location!.ToString()); Assert.AreEqual(HttpStatusCode.OK, poll.StatusCode); Assert.AreEqual(1, f.Directories.TenantStatusWrites);
        var other = await App(f, "bindry"); using var cross = await f.Get(response.Headers.Location.ToString().Replace(app.ToString(), other.ToString())); Assert.AreEqual(HttpStatusCode.BadRequest, cross.StatusCode);
    }
    [TestMethod]
    public async Task PermissionLossAfterCommitDoesNotReturnTenantState()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerTenantStatus: true); var app = await App(f);
        var identity = (f.Data.Access.Org, app, f.Data.Access.Reader); var old = f.Data.Access.Access.Rows[identity];
        f.Directories.DuringMutation = _ => { f.Data.Access.Access.Rows[identity] = old with { Permissions = [] }; return Task.CompletedTask; };
        using var response = await Write(f, app); Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode); Assert.AreEqual(1, f.Directories.TenantStatusWrites);
        Assert.DoesNotContain("Enterprise", await response.Content.ReadAsStringAsync());
    }
}
