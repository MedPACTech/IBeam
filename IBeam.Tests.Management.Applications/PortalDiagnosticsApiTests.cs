using System.Net;
using System.Text.Json;
using IBeam.Management;
namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class PortalDiagnosticsApiTests
{
    [TestMethod]
    public async Task PortalRoutesAreOptionalAndNamedHumanSchemeIsMandatory()
    {
        await using var off = await PortalHttpFixture.CreateAsync(install: false);
        Assert.AreEqual(HttpStatusCode.NotFound, (await off.Get("/api/management/applications/" + Guid.NewGuid() + "/system-errors")).StatusCode);
        await using var f = await PortalHttpFixture.CreateAsync();
        var app = await f.Data.AddApp();
        var path = "/api/management/applications/" + app + "/system-errors?pageSize=1";
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await f.Get(path, "customer")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get(path, "machine")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get(path, "other")).StatusCode);
        Assert.AreEqual(0, f.Data.Backend.Calls);
        using var response = await f.Get(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("data")[0].GetProperty("id").GetString();
        Assert.AreEqual(HttpStatusCode.OK, (await f.Get("/api/management/applications/" + app + "/system-errors/" + id)).StatusCode);
        Assert.DoesNotContain(ConnectionFixture.Key, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await f.Get(path + "&continuationToken=not-a-token")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await f.Get(path.Replace("pageSize=1", "pageSize=secret-do-not-echo"))).StatusCode);
    }
    [TestMethod]
    public async Task KeysUrlsAndOrganizationOverridesCannotChangeServerSelectedTarget()
    {
        await using var f = await PortalHttpFixture.CreateAsync();
        var app = await f.Data.AddApp("qurvia", ManagementOperations.ErrorsRead);
        using var response = await f.Get("/api/management/applications/" + app +
            "/system-errors?pageSize=1&url=https://evil.example&organizationId=" + f.OtherOrg + "&apiKey=wrong");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get("/api/management/applications/" + app + "/system-logs?pageSize=1")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get("/api/management/applications/" + app + "/system-errors?pageSize=1", "foreign")).StatusCode);
        var grant = f.Data.Access.Access.Rows[(f.Data.Access.Org, app, f.Data.Access.Reader)];
        f.Data.Access.Access.Rows[(f.Data.Access.Org, app, f.Data.Access.Reader)] = grant with { RevokedUtc = DateTimeOffset.UtcNow };
        var calls = f.Data.Backend.Calls; var reads = f.Data.Secrets.Reads;
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get("/api/management/applications/" + app + "/system-errors?pageSize=1")).StatusCode);
        Assert.AreEqual(calls, f.Data.Backend.Calls); Assert.AreEqual(reads, f.Data.Secrets.Reads);
    }
}
