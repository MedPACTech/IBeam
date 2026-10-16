using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Models;
namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class PortalDiagnosticsTests
{
    [TestMethod]
    public async Task ListDetailAndContinuationAreAppUserOperationAndConnectionBound()
    {
        using var f = new PortalFixture(); var a = await f.AddApp(); var b = await f.AddApp("bindry");
        var page = await f.Service.ErrorsAsync(a, f.Query());
        Assert.HasCount(1, page.Items); Assert.IsNotNull(page.ContinuationToken);
        Assert.AreNotEqual("d1_targetqurvia", page.Items[0].Id);
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(page));
        var detail = await f.Service.ErrorAsync(a, page.Items[0].Id);
        Assert.AreEqual(page.Items[0].Id, detail.Id);
        await f.Service.ErrorsAsync(a, new() { Cursor = page.ContinuationToken });
        var calls = f.Backend.Calls;
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Service.ErrorAsync(b, page.Items[0].Id));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Service.ErrorsAsync(b, new() { Cursor = page.ContinuationToken }));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Service.LogAsync(a, page.Items[0].Id));
        Assert.AreEqual(calls, f.Backend.Calls);
        await Assert.ThrowsAsync<DiagnosticQueryException>(() => f.Service.ErrorsAsync(a, f.Query(page.ContinuationToken) with { PageSize = 2 }));
        var row = f.Access.Apps.Rows[(f.Access.Org, a)];
        f.Access.Apps.Rows[(f.Access.Org, a)] = row with { Version = "changed" };
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Service.ErrorAsync(a, page.Items[0].Id));
        f.Access.Apps.Rows[(f.Access.Org, a)] = row;
        f.Clock.Now = f.Clock.Now.AddHours(1);
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Service.ErrorAsync(a, page.Items[0].Id));
    }
    [TestMethod]
    public async Task RevokedOrUnassignedReadsNeverReachSecretStoreOrTargetAndInfoRevocationStopsData()
    {
        using var f = new PortalFixture(); var app = await f.AddApp("qurvia", ManagementOperations.ErrorsRead);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.LogsAsync(app, new()));
        Assert.AreEqual(0, f.Backend.Calls); Assert.AreEqual(0, f.Secrets.Reads);
        f.Backend.DuringInfo = _ =>
        {
            f.Access.Users[(f.Access.Org, f.Access.Reader)] = f.Access.Users[(f.Access.Org, f.Access.Reader)] with { IsEnabled = false };
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.ErrorsAsync(app, f.Query()));
        Assert.AreEqual(0, f.Backend.DataCalls);
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Access.Audit));
    }
    [TestMethod]
    [DataRow(429, typeof(ManagementPortalRateLimitException))]
    [DataRow(503, typeof(ManagementDiagnosticUnavailableException))]
    [DataRow(401, typeof(ManagementAccessDeniedException))]
    [DataRow(302, typeof(ManagementPortalResponseException))]
    public async Task RemoteFailuresNeverExposeRawBodies(int code, Type type)
    {
        using var f = new PortalFixture(); var app = await f.AddApp();
        f.Backend.Status = (HttpStatusCode)code;
        Exception? failure = null;
        try { await f.Service.ErrorsAsync(app, f.Query()); } catch (Exception ex) { failure = ex; }
        Assert.IsNotNull(failure); Assert.AreEqual(type, failure.GetType());
        Assert.DoesNotContain(ConnectionFixture.Key, failure.ToString());
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Access.Audit));
    }
    [TestMethod]
    public async Task WrongIdentityMalformedResponsesAndCancellationAreSafe()
    {
        using var f = new PortalFixture(); var app = await f.AddApp();
        f.Backend.WrongIdentity = true;
        await Assert.ThrowsAsync<ManagementPortalResponseException>(() => f.Service.ErrorsAsync(app, f.Query()));
        Assert.AreEqual(0, f.Backend.DataCalls);
        f.Backend.WrongIdentity = false; f.Backend.CorruptBody = true;
        await Assert.ThrowsAsync<ManagementDiagnosticUnavailableException>(() => f.Service.ErrorsAsync(app, f.Query()));
        f.Backend.CorruptBody = false; f.Backend.DuringInfo = ct => Task.Delay(Timeout.Infinite, ct);
        await Assert.ThrowsAsync<ManagementConnectionTimeoutException>(() => f.Service.ErrorsAsync(app, f.Query()));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.ErrorsAsync(app, f.Query(), cancel.Token));
    }
}
