using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Entities;
namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class ApplicationSelectorApiTests
{
    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.Clone();
    }
    [TestMethod]
    public async Task SingleSessionSwitchesThreeAppsWithIndependentPermissionsAndRevocation()
    {
        await using var f = await PortalHttpFixture.CreateAsync(selector: true);
        var q = await f.Data.AddApp("qurvia", ManagementOperations.ErrorsRead);
        var b = await f.Data.AddApp("bindry", ManagementOperations.LogsRead);
        f.Data.NetworkOptions.ApprovedOrigins = ["https://api.qurvia.app", "https://api.bindry.app", "https://api.medinsights.app"];
        var m = await f.Data.AddApp("medinsights");
        var before = f.Data.Secrets.Reads;
        var selected = await Body(await f.Get("/api/management/applications"));
        Assert.AreEqual(3, selected.GetProperty("data").GetArrayLength());
        var qrow = selected.GetProperty("data").EnumerateArray().Single(x => x.GetProperty("applicationId").GetGuid() == q);
        Assert.AreEqual(ManagementOperations.ErrorsRead, qrow.GetProperty("permissions")[0].GetString());
        Assert.IsFalse(qrow.TryGetProperty("baseUrl", out _)); Assert.IsFalse(qrow.TryGetProperty("secretReference", out _));
        Assert.AreEqual(before, f.Data.Secrets.Reads); Assert.AreEqual(0, f.Data.Backend.Calls);
        foreach (var (app, kind) in new[] { (q, "system-errors"), (b, "system-logs"), (m, "system-errors"), (q, "system-errors") })
            Assert.AreEqual(HttpStatusCode.OK, (await f.Get($"/api/management/applications/{app}/{kind}?pageSize=1")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get($"/api/management/applications/{q}/system-logs?pageSize=1")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get($"/api/management/applications/{b}/system-errors?pageSize=1")).StatusCode);
        var key = (f.Data.Access.Org, q, f.Data.Access.Reader);
        f.Data.Access.Access.Rows[key] = f.Data.Access.Access.Rows[key] with { RevokedUtc = DateTimeOffset.UtcNow };
        selected = await Body(await f.Get("/api/management/applications"));
        Assert.AreEqual(2, selected.GetProperty("data").GetArrayLength());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get($"/api/management/applications/{q}/system-errors?pageSize=1")).StatusCode);
    }
    [TestMethod]
    public async Task UsersOrganizationsAndAdministrationDoNotConferDiagnosticAccess()
    {
        await using var f = await PortalHttpFixture.CreateAsync(selector: true);
        var q = await f.Data.AddApp(); var b = await f.Data.AddApp("bindry");
        await f.Data.Access.Access.CreateAsync(new() { OrganizationId = f.Data.Access.Org, ApplicationId = b, UserId = f.OtherUser, Permissions = [ManagementOperations.LogsRead] });
        var selected = await Body(await f.Get("/api/management/applications", "other"));
        Assert.AreEqual(b, selected.GetProperty("data")[0].GetProperty("applicationId").GetGuid());
        Assert.AreEqual(1, selected.GetProperty("data").GetArrayLength());
        Assert.AreEqual(0, (await Body(await f.Get("/api/management/applications", "foreign"))).GetProperty("data").GetArrayLength());
        Assert.AreEqual(0, (await Body(await f.Get("/api/management/applications", "admin"))).GetProperty("data").GetArrayLength());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get($"/api/management/applications/{q}/system-errors?pageSize=1", "other")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get("/api/management/applications/registry")).StatusCode);
        Assert.AreEqual(2, (await Body(await f.Get("/api/management/applications/registry", "admin"))).GetProperty("data").GetArrayLength());
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await f.Get("/api/management/applications", "customer")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get("/api/management/applications", "machine")).StatusCode);
        f.Data.Access.Users[(f.Data.Access.Org, f.Data.Access.Reader)] = new(f.Data.Access.Org, f.Data.Access.Reader, false, false);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get("/api/management/applications")).StatusCode);
    }
    [TestMethod]
    public async Task SelectorCursorsAreUserOrgSizeAndExpiryBoundAndDisabledConnectionsDisappear()
    {
        await using var f = await PortalHttpFixture.CreateAsync(selector: true);
        var q = await f.Data.AddApp(); var b = await f.Data.AddApp("bindry");
        var first = await Body(await f.Get("/api/management/applications?pageSize=1"));
        var cursor = first.GetProperty("continuationToken").GetString(); Assert.IsNotNull(cursor);
        var path = "/api/management/applications?pageSize=1&continuationToken=" + cursor;
        var second = await Body(await f.Get(path));
        Assert.AreNotEqual(first.GetProperty("data")[0].GetProperty("applicationId").GetGuid(), second.GetProperty("data")[0].GetProperty("applicationId").GetGuid());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await f.Get(path, "other")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await f.Get(path, "foreign")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await f.Get(path.Replace("pageSize=1", "pageSize=2"))).StatusCode);
        f.Data.Clock.Now = f.Data.Clock.Now.AddHours(1);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await f.Get(path)).StatusCode);
        var key = (f.Data.Access.Org, q); f.Data.Access.Apps.Rows[key] = f.Data.Access.Apps.Rows[key] with { IsEnabled = false };
        key = (f.Data.Access.Org, b); f.Data.Access.Apps.Rows[key] = f.Data.Access.Apps.Rows[key] with { ActiveCredentialId = null };
        Assert.AreEqual(0, (await Body(await f.Get("/api/management/applications"))).GetProperty("data").GetArrayLength());
    }
}
