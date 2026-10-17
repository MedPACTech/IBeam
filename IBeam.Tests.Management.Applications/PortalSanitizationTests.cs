using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Models;
namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class PortalSanitizationTests
{
    private static void Testing(PortalFixture f, Guid app)
    {
        var row = f.Access.Apps.Rows[(f.Access.Org, app)]; f.Access.Apps.Rows[(f.Access.Org, app)] = row with { Environment = "Testing" };
        var host = new Uri(row.BaseUrl).Host; var target = f.Backend.Targets[host]; f.Backend.Targets[host] = (target.Key, "Testing", target.Secret);
    }
    [TestMethod]
    public async Task AdvertisedRawFieldsSurviveBothProjectionsWithIndependentModuleModesAndSafeAudit()
    {
        await using var f = await PortalHttpFixture.CreateAsync(audit: true);
        var app = await f.Data.AddApp(); Testing(f.Data, app);
        f.Data.Backend.IncludeRawBody = true; f.Data.Backend.UnsanitizedCapabilities = [ManagementOperations.ErrorsRead];
        var route = $"/api/management/applications/{app}/system-errors";
        using var response = await f.Get(route + "?pageSize=1&sanitization.enabled=true"); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var row = page.RootElement.GetProperty("data")[0];
        Assert.AreEqual(ConnectionFixture.Key, row.GetProperty("unsanitized").GetProperty("message").GetString());
        Assert.DoesNotContain(ConnectionFixture.Key, row.GetProperty("summary").GetString()!);
        using var detail = await f.Get(route + "/" + row.GetProperty("id").GetString()); Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        using var detailBody = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()); Assert.IsTrue(detailBody.RootElement.GetProperty("data").TryGetProperty("unsanitized", out _));
        using var logs = await f.Get($"/api/management/applications/{app}/system-logs?pageSize=1&includeUnsanitized=true"); Assert.AreEqual(HttpStatusCode.OK, logs.StatusCode);
        Assert.DoesNotContain(ConnectionFixture.Key, await logs.Content.ReadAsStringAsync());
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Audit.Rows));
        f.Data.Backend.UnsanitizedCapabilities = [ManagementOperations.LogsRead];
        using var rawLogs = await f.Get($"/api/management/applications/{app}/system-logs?pageSize=1");
        using var logBody = JsonDocument.Parse(await rawLogs.Content.ReadAsStringAsync()); Assert.AreEqual(ConnectionFixture.Key, logBody.RootElement.GetProperty("data")[0].GetProperty("unsanitized").GetProperty("detail").GetString());
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Audit.Rows));
    }
    [TestMethod]
    public async Task ModeChangesInvalidatePortalLinksAndCursorsBeforeDataRequests()
    {
        using var f = new PortalFixture(); var app = await f.AddApp(); Testing(f, app); f.Backend.IncludeRawBody = true;
        var page = await f.Service.ErrorsAsync(app, f.Query()); Assert.IsNull(page.Items[0].Unsanitized);
        f.Backend.UnsanitizedCapabilities = [ManagementOperations.ErrorsRead]; var calls = f.Backend.DataCalls;
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Service.ErrorAsync(app, page.Items[0].Id));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Service.ErrorsAsync(app, new() { Cursor = page.ContinuationToken }));
        Assert.AreEqual(calls, f.Backend.DataCalls);
        var raw = await f.Service.ErrorsAsync(app, f.Query()); Assert.IsNotNull(raw.Items[0].Unsanitized);
        f.Backend.UnsanitizedCapabilities = []; calls = f.Backend.DataCalls;
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Service.ErrorAsync(app, raw.Items[0].Id));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Service.ErrorsAsync(app, new() { Cursor = raw.ContinuationToken }));
        Assert.AreEqual(calls, f.Backend.DataCalls); Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Access.Audit));
    }
    [TestMethod]
    public async Task UnadvertisedRawIsRemovedAndInvalidProductionOrCapabilityDeclarationsFailClosed()
    {
        using var f = new PortalFixture(); var app = await f.AddApp(); f.Backend.IncludeRawBody = true;
        Assert.IsNull((await f.Service.ErrorsAsync(app, f.Query())).Items[0].Unsanitized);
        f.Backend.UnsanitizedCapabilities = [ManagementOperations.ErrorsRead]; var calls = f.Backend.DataCalls;
        await Assert.ThrowsAsync<ManagementPortalResponseException>(() => f.Service.ErrorsAsync(app, f.Query())); Assert.AreEqual(calls, f.Backend.DataCalls);
        Testing(f, app);
        foreach(var caps in new[] { new[] { ManagementOperations.ErrorsRead, ManagementOperations.ErrorsRead }, new[] { ManagementOperations.DiagnosticsInfo }, new[] { "*" } })
        { f.Backend.UnsanitizedCapabilities = caps; await Assert.ThrowsAsync<ManagementPortalResponseException>(() => f.Service.ErrorsAsync(app, f.Query())); }
        f.Backend.UnsanitizedCapabilities = [ManagementOperations.ErrorsRead]; f.Backend.RawMessage = new string('x', 32769);
        await Assert.ThrowsAsync<ManagementPortalResponseException>(() => f.Service.ErrorsAsync(app, f.Query()));
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Access.Audit));
    }
}
