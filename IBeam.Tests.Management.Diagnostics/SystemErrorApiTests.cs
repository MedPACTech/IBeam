using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class SystemErrorApiTests
{
    private const string Route = "/api/management-diagnostics/system-errors";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OptedInRoutesPageAndReturnOnlyApprovedMetadata(bool featureFirst)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(featureFirst: featureFirst);
        var row = ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-2)) with { Source = "RequestPipeline", TraceId = SystemErrorApiFixture.Sensitive };
        f.Rows.Rows.Add(row);
        f.Rows.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1)) with { Source = SystemErrorApiFixture.Sensitive });
        var issued = await f.IssueAsync(ManagementOperations.ErrorsRead, ManagementOperations.DiagnosticsInfo);
        using var response = await f.SendAsync(Route + "?pageSize=1", issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        var first = json.RootElement.GetProperty("data")[0];
        CollectionAssert.AreEquivalent(new[] { "id", "occurredAtUtc", "source", "summary", "correlationId" },
            first.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.AreEqual("RequestPipeline", first.GetProperty("source").GetString());
        Assert.AreEqual(TimeSpan.Zero, first.GetProperty("occurredAtUtc").GetDateTimeOffset().Offset);
        Assert.IsTrue(first.GetProperty("correlationId").GetString()!.StartsWith("c1_", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(row.RowKey, StringComparison.Ordinal));
        var id = first.GetProperty("id").GetString()!;
        using var detail = await f.SendAsync(Route + "/" + id, issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.AreEqual(first.GetRawText(), detailJson.RootElement.GetProperty("data").GetRawText());
        var cursor = json.RootElement.GetProperty("continuationToken").GetString()!;
        using var next = await f.SendAsync(Route + "?continuationToken=" + cursor, issued.ApiKey);
        using var nextJson = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        Assert.AreEqual("System", nextJson.RootElement.GetProperty("data")[0].GetProperty("source").GetString());
        Assert.AreEqual(JsonValueKind.Null, nextJson.RootElement.GetProperty("continuationToken").ValueKind);
        using var info = await f.SendAsync("/api/management-diagnostics/info", issued.ApiKey);
        using var infoJson = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        CollectionAssert.AreEquivalent(new[] { ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead },
            infoJson.RootElement.GetProperty("data").GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.IsTrue(f.Audits.Transactions.Any(x => x.Action == ManagementOperations.ErrorsRead && x.Succeeded));
        var audits = JsonSerializer.Serialize(f.Audits.Transactions);
        Assert.IsFalse(audits.Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        Assert.IsFalse(audits.Contains(id, StringComparison.Ordinal));
        Assert.IsTrue(f.Audits.Transactions.All(x => x.BeforeJson is null && x.AfterJson is null));
    }

    [TestMethod]
    public async Task BaseApiKeepsErrorRoutesAbsentAndCustomerAuthenticationUnchanged()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installed: false);
        var issued = await f.IssueAsync(ManagementOperations.DiagnosticsInfo);
        using var errors = await f.SendAsync(Route, issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.NotFound, errors.StatusCode);
        using var info = await f.SendAsync("/api/management-diagnostics/info", issued.ApiKey);
        using var json = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetProperty("capabilities").GetArrayLength());
        Assert.AreEqual("Customer", (await f.App.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync())!.Name);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/customer-only");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "customer");
        using var customer = await f.Client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, customer.StatusCode);
    }

    [TestMethod]
    [DataRow("missing", 401)]
    [DataRow("customer", 401)]
    [DataRow("human", 401)]
    [DataRow("legacy", 401)]
    [DataRow("info-only", 403)]
    [DataRow("logs-only", 403)]
    [DataRow("revoked", 401)]
    [DataRow("expired", 401)]
    [DataRow("other-deployment", 401)]
    public async Task UnauthorizedListAndDetailNeverReadErrors(string mode, int status)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync();
        var issued = await f.IssueAsync(mode == "info-only" ? ManagementOperations.DiagnosticsInfo :
            mode == "logs-only" ? ManagementOperations.LogsRead : ManagementOperations.ErrorsRead);
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
        foreach(var path in new[] { Route, Route + "/invalid-id" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (mode is "customer" or "human") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mode);
            else if (mode != "missing") request.Headers.Add("X-IBeam-Management-Key", mode == "legacy" ? "ibeam_legacy_tenant_secret" : issued.ApiKey);
            using var response = await f.Client.SendAsync(request);
            Assert.AreEqual(status, (int)response.StatusCode);
        }
        Assert.HasCount(0, f.Rows.Queries);
        Assert.AreEqual(0, f.Rows.Gets);
    }

    [TestMethod]
    [DataRow("pageSize=fixture-private-payload-password")]
    [DataRow("fromUtc=fixture-private-payload-password")]
    [DataRow("toUtc=fixture-private-payload-password")]
    [DataRow("pageSize=1001")]
    [DataRow("pageSize=0")]
    [DataRow("continuationToken=fixture-private-payload-password")]
    public async Task QueryValidationReturnsSafeEnvelopesWithoutEchoingInputs(string query)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync();
        var issued = await f.IssueAsync();
        using var response = await f.SendAsync(Route + "?" + query, issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(body.Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual("Management.InvalidQuery", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        Assert.HasCount(0, f.Rows.Queries);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("tampered")]
    [DataRow("expired")]
    [DataRow("other-source")]
    public async Task InvalidAndMissingDetailsHaveTheSameSafeNotFoundResponse(string change)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync();
        f.Rows.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1)));
        var issued = await f.IssueAsync();
        using var list = await f.SendAsync(Route, issued.ApiKey);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var id = listJson.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
        if (change == "missing") f.Rows.Rows.Clear();
        if (change == "tampered") id = "invalid-" + SystemErrorApiFixture.Sensitive;
        if (change == "expired") f.Clock.NowUtc = ErrorReaderFixture.Now.AddMinutes(31);
        if (change == "other-source") f.Rows.SourceBinding = "another-table";
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
        await using var f = await SystemErrorApiFixture.CreateAsync();
        f.Rows.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1)));
        var issued = await f.IssueAsync();
        using var list = await f.SendAsync(Route, issued.ApiKey);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var id = listJson.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
        f.SimulateStorageFailure = true;
        foreach(var path in new[] { Route, Route + "/" + id })
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
        await using var f = await SystemErrorApiFixture.CreateAsync(configure: values =>
            values["IBeam:Management:Diagnostics:Errors:RequestTimeoutSeconds"] = "1");
        var issued = await f.IssueAsync();
        f.Rows.DelayUntilCancellation = true;
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
    public async Task NoErrorWriteRoutesAreInstalled(string method)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync();
        var issued = await f.IssueAsync();
        foreach(var path in new[] { Route, Route + "/id" })
        {
            using var response = await f.SendAsync(path, issued.ApiKey, new HttpMethod(method));
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
        Assert.HasCount(0, f.Rows.Queries);
        Assert.AreEqual(0, f.Rows.Gets);
    }
}
