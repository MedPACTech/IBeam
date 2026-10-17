using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Models;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class SystemLogApiTests
{
    private const string Route = "/api/management-diagnostics/system-logs";

    [TestMethod]
    public async Task OptedInRoutesPageAndReturnOnlyApprovedMetadata()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true);
        var row = LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-2), trace: "0HND11FN7L0DA:00000001");
        f.LogRows.Rows.Add(row);
        f.LogRows.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1), source: SystemErrorApiFixture.Sensitive, level: "Warning"));
        var issued = await f.IssueAsync(ManagementOperations.LogsRead, ManagementOperations.DiagnosticsInfo);
        using var response = await f.SendAsync(Route + "?pageSize=1", issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        var first = json.RootElement.GetProperty("data")[0];
        CollectionAssert.AreEquivalent(new[] { "id", "occurredAtUtc", "level", "source", "summary", "correlationId" },
            first.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.AreEqual("RequestPipeline", first.GetProperty("source").GetString());
        Assert.AreEqual("Error", first.GetProperty("level").GetString());
        Assert.IsTrue(first.GetProperty("correlationId").GetString()!.StartsWith("c1_", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(row.RowKey, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(row.TraceId, StringComparison.Ordinal));
        var id = first.GetProperty("id").GetString()!;
        using var detail = await f.SendAsync(Route + "/" + id, issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.AreEqual(first.GetRawText(), detailJson.RootElement.GetProperty("data").GetRawText());
        var cursor = json.RootElement.GetProperty("continuationToken").GetString()!;
        using var next = await f.SendAsync(Route + "?continuationToken=" + cursor, issued.ApiKey);
        var nextText = await next.Content.ReadAsStringAsync();
        using var nextJson = JsonDocument.Parse(nextText);
        Assert.AreEqual("System", nextJson.RootElement.GetProperty("data")[0].GetProperty("source").GetString());
        Assert.IsFalse(nextText.Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        using var info = await f.SendAsync("/api/management-diagnostics/info", issued.ApiKey);
        using var infoJson = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        CollectionAssert.AreEquivalent(new[] { ManagementOperations.DiagnosticsInfo, ManagementOperations.LogsRead },
            infoJson.RootElement.GetProperty("data").GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.IsTrue(f.Audits.Transactions.Any(x => x.Action == ManagementOperations.LogsRead && x.Succeeded));
        var audits = JsonSerializer.Serialize(f.Audits.Transactions);
        Assert.IsFalse(audits.Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        Assert.IsFalse(audits.Contains(id, StringComparison.Ordinal));
        Assert.IsTrue(f.Audits.Transactions.All(x => x.BeforeJson is null && x.AfterJson is null));
    }

    [TestMethod]
    public async Task FiltersRoundTripThroughHttpAndAreNotEchoedOrAudited()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true);
        const string trace = "customer-reported-trace:0001";
        f.LogRows.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-2), trace: trace));
        f.LogRows.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1), level: "Critical", source: "AppDomain.UnhandledException"));
        var issued = await f.IssueAsync(ManagementOperations.LogsRead);
        using var byTrace = await f.SendAsync(Route + "?traceId=" + Uri.EscapeDataString(trace), issued.ApiKey);
        var body = await byTrace.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
        Assert.IsFalse(body.Contains(trace, StringComparison.Ordinal));
        using var byLevel = await f.SendAsync(Route + "?level=Critical&source=AppDomain.UnhandledException", issued.ApiKey);
        using var levelJson = JsonDocument.Parse(await byLevel.Content.ReadAsStringAsync());
        Assert.AreEqual("Critical", levelJson.RootElement.GetProperty("data")[0].GetProperty("level").GetString());
        Assert.AreEqual(1, levelJson.RootElement.GetProperty("data").GetArrayLength());
        Assert.IsFalse(JsonSerializer.Serialize(f.Audits.Transactions).Contains(trace, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ErrorAndLogModulesComposeCapabilitiesAndStayIndependentlyGranted()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installLogs: true);
        f.LogRows.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1)));
        f.Rows.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1)));
        var all = await f.IssueAsync(ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead);
        using var info = await f.SendAsync("/api/management-diagnostics/info", all.ApiKey);
        using var infoJson = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        CollectionAssert.AreEquivalent(new[] { ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead },
            infoJson.RootElement.GetProperty("data").GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).ToArray());
        var logsOnly = await f.IssueAsync(ManagementOperations.LogsRead);
        var errorsOnly = await f.IssueAsync(ManagementOperations.ErrorsRead);
        using (var r = await f.SendAsync(Route, logsOnly.ApiKey)) Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
        using (var r = await f.SendAsync("/api/management-diagnostics/system-errors", logsOnly.ApiKey)) Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
        using (var r = await f.SendAsync(Route, errorsOnly.ApiKey)) Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
        // An error record ID is not a log ID, even on the same deployment.
        using var errors = await f.SendAsync("/api/management-diagnostics/system-errors", all.ApiKey);
        using var errorsJson = JsonDocument.Parse(await errors.Content.ReadAsStringAsync());
        var errorId = errorsJson.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
        using var crossType = await f.SendAsync(Route + "/" + errorId, all.ApiKey);
        Assert.AreEqual(HttpStatusCode.NotFound, crossType.StatusCode);
        using var crossCursor = await f.SendAsync(Route + "?continuationToken=" + errorId, all.ApiKey);
        Assert.AreEqual(HttpStatusCode.BadRequest, crossCursor.StatusCode);
    }

    [TestMethod]
    public async Task BaseAndErrorOnlyRegistrationKeepLogRoutesAbsent()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync();
        var issued = await f.IssueAsync(ManagementOperations.LogsRead, ManagementOperations.DiagnosticsInfo);
        using var logs = await f.SendAsync(Route, issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.NotFound, logs.StatusCode);
        using var info = await f.SendAsync("/api/management-diagnostics/info", issued.ApiKey);
        using var json = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("capabilities").EnumerateArray()
            .Any(x => x.GetString() == ManagementOperations.LogsRead));
    }

    [TestMethod]
    [DataRow("missing", 401)]
    [DataRow("customer", 401)]
    [DataRow("human", 401)]
    [DataRow("legacy", 401)]
    [DataRow("info-only", 403)]
    [DataRow("errors-only", 403)]
    [DataRow("revoked", 401)]
    [DataRow("expired", 401)]
    [DataRow("other-deployment", 401)]
    public async Task UnauthorizedListAndDetailNeverReadLogs(string mode, int status)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true);
        var issued = await f.IssueAsync(mode == "info-only" ? ManagementOperations.DiagnosticsInfo :
            mode == "errors-only" ? ManagementOperations.ErrorsRead : ManagementOperations.LogsRead);
        if (mode is "revoked" or "expired" or "other-deployment")
        {
            var row = (await f.Credentials.GetAsync("qurvia", "Production", issued.Credential.CredentialId))!;
            await f.Credentials.ReplaceAsync(mode switch
            {
                "revoked" => row with { Status = "Revoked", RevokedUtc = DateTimeOffset.UtcNow },
                "expired" => row with { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) },
                _ => row with { Environment = "Test" }
            }, row.Version);
        }
        foreach (var path in new[] { Route, Route + "/invalid-id" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (mode is "customer" or "human") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mode);
            else if (mode != "missing") request.Headers.Add("X-IBeam-Management-Key", mode == "legacy" ? "ibeam_legacy_tenant_secret" : issued.ApiKey);
            using var response = await f.Client.SendAsync(request);
            Assert.AreEqual(status, (int)response.StatusCode);
        }
        Assert.HasCount(0, f.LogRows.Queries);
        Assert.AreEqual(0, f.LogRows.Gets);
    }

    [TestMethod]
    [DataRow("pageSize=fixture-private-payload-password")]
    [DataRow("fromUtc=fixture-private-payload-password")]
    [DataRow("pageSize=0")]
    [DataRow("continuationToken=fixture-private-payload-password")]
    [DataRow("level=fixture-private-payload-password")]
    [DataRow("source=fixture-private-payload-password")]
    [DataRow("traceId=fixture-private-payload-password%27%20or%20true")]
    [DataRow("fromUtc=2026-10-01T00:00:00Z&toUtc=2026-10-16T00:00:00Z")]
    public async Task QueryValidationReturnsSafeEnvelopesWithoutEchoingInputs(string query)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true);
        var issued = await f.IssueAsync(ManagementOperations.LogsRead);
        using var response = await f.SendAsync(Route + "?" + query, issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(body.Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual("Management.InvalidQuery", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.HasCount(0, f.LogRows.Queries);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("tampered")]
    [DataRow("expired")]
    [DataRow("other-source")]
    public async Task InvalidAndMissingDetailsHaveTheSameSafeNotFoundResponse(string change)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true);
        f.LogRows.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1)));
        var issued = await f.IssueAsync(ManagementOperations.LogsRead);
        using var list = await f.SendAsync(Route, issued.ApiKey);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var id = listJson.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
        if (change == "missing") f.LogRows.Rows.Clear();
        if (change == "tampered") id = "invalid-" + SystemErrorApiFixture.Sensitive;
        if (change == "expired") f.Clock.NowUtc = LogReaderFixture.Now.AddMinutes(31);
        if (change == "other-source") f.LogRows.SourceBinding = "another-table";
        using var detail = await f.SendAsync(Route + "/" + id, issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.NotFound, detail.StatusCode);
        var body = await detail.Content.ReadAsStringAsync();
        Assert.IsFalse(body.Contains(id, StringComparison.Ordinal));
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual("Management.DiagnosticNotFound", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task ProviderFailuresAreSanitizedEvenWithDetailedHostErrorsAndAuditEnabled()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true);
        f.LogRows.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1)));
        var issued = await f.IssueAsync(ManagementOperations.LogsRead);
        using var list = await f.SendAsync(Route, issued.ApiKey);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var id = listJson.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
        f.SimulateStorageFailure = true;
        foreach (var path in new[] { Route, Route + "/" + id })
        {
            using var response = await f.SendAsync(path, issued.ApiKey);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.IsFalse(body.Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
            Assert.IsFalse(body.Contains("provider", StringComparison.Ordinal));
        }
        Assert.IsFalse(JsonSerializer.Serialize(f.Audits.Transactions).Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        Assert.IsFalse(string.Join(" ", f.Logs.Messages).Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DeadlineReturnsSafeTimeoutAndClientCancellationRemainsCancellation()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true, configure: values =>
            values["IBeam:Management:Diagnostics:Logs:RequestTimeoutSeconds"] = "1");
        var issued = await f.IssueAsync(ManagementOperations.LogsRead);
        f.LogRows.DelayUntilCancellation = true;
        using var timedOut = await f.SendAsync(Route, issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.GatewayTimeout, timedOut.StatusCode);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.SendAsync(Route, issued.ApiKey, ct: cancellation.Token));
    }

    [TestMethod]
    [DataRow("POST")]
    [DataRow("PUT")]
    [DataRow("PATCH")]
    [DataRow("DELETE")]
    public async Task NoLogWriteRoutesAreInstalled(string method)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true);
        var issued = await f.IssueAsync(ManagementOperations.LogsRead);
        foreach (var path in new[] { Route, Route + "/id" })
        {
            using var response = await f.SendAsync(path, issued.ApiKey, new HttpMethod(method));
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
        Assert.HasCount(0, f.LogRows.Queries);
        Assert.AreEqual(0, f.LogRows.Gets);
    }

    [TestMethod]
    public async Task DirectPublicServiceCallsRequireManagementContextEvenWithoutControllers()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true);
        using var scope = f.App.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemLogReaderService>();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.ListAsync(new SystemLogQuery()));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.GetAsync("untrusted-id"));
        Assert.HasCount(0, f.LogRows.Queries);
        Assert.AreEqual(0, f.LogRows.Gets);
    }

    [TestMethod]
    public async Task MissingLogRepositoryFailsAtStartup()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true, registerRepository: false));
    }
}
