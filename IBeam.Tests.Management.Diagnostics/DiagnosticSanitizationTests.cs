using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Models;
using IBeam.Management.Services;
using IBeam.Management.Services.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.Diagnostics;
internal sealed class SanitizationHost(string name = "Testing") : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "ManagementTests";
    public string ContentRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
[TestClass]
public sealed class DiagnosticSanitizationTests
{
    private const string Canary = SystemErrorApiFixture.Sensitive;
    private static void Seed(SystemErrorApiFixture f)
    {
        f.Rows.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1)) with { Source = Canary, TraceId = Canary, Message = Canary, Exception = Canary, Path = Canary, Method = Canary });
        f.LogRows.Rows.Add(LogReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1)) with { Source = Canary, TraceId = Canary, Message = Canary, Detail = Canary });
    }
    [TestMethod, DataRow(null), DataRow(""), DataRow("  "), DataRow("true")]
    public async Task MissingBlankOrTrueKeepsOriginalShapeAndNeverEmitsRawFields(string? value)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installLogs: true, managementAudit: true, configure: values => {
            if (value is not null) foreach(var kind in new[] { "Errors", "Logs" }) values[$"IBeam:Management:Diagnostics:{kind}:Sanitization:Enabled"] = value;
        });
        Seed(f); var key = await f.IssueAsync(ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead);
        foreach(var kind in new[] { "system-errors", "system-logs" })
        {
            using var response = await f.SendAsync("/api/management-diagnostics/" + kind, key.ApiKey);
            var raw = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); Assert.DoesNotContain(Canary, raw);
            using var json = JsonDocument.Parse(raw); Assert.IsFalse(json.RootElement.GetProperty("data")[0].TryGetProperty("unsanitized", out _));
        }
        using var info = await f.SendAsync("/api/management-diagnostics/info", key.ApiKey);
        using var body = JsonDocument.Parse(await info.Content.ReadAsStringAsync()); Assert.IsFalse(body.RootElement.GetProperty("data").TryGetProperty("unsanitizedCapabilities", out _));
        Assert.IsFalse(f.Rows.Queries.Any(x => x.IncludeUnsanitized)); Assert.IsFalse(f.LogRows.Queries.Any(x => x.IncludeUnsanitized));
    }
    [TestMethod, DataRow(true, false), DataRow(false, true), DataRow(true, true)]
    public async Task ExplicitFalseIsIndependentAdvertisedAndReturnsRawListDetailWithoutAuditLeak(bool errorRaw, bool logRaw)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installLogs: true, managementAudit: true, configure: values => {
            values["IBeam:Management:Diagnostics:Environment"] = "Testing";
            values["IBeam:Management:Diagnostics:Errors:Sanitization:Enabled"] = errorRaw ? "false" : "true";
            values["IBeam:Management:Diagnostics:Logs:Sanitization:Enabled"] = logRaw ? "false" : "true";
        });
        Seed(f); var key = await f.IssueAsync(ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead);
        foreach(var (kind, enabled) in new[] { ("system-errors", errorRaw), ("system-logs", logRaw) })
        {
            var route = "/api/management-diagnostics/" + kind;
            using var response = await f.SendAsync(route, key.ApiKey); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var row = json.RootElement.GetProperty("data")[0];
            Assert.AreEqual(enabled, row.TryGetProperty("unsanitized", out var raw));
            if (enabled) { Assert.AreEqual(Canary, raw.GetProperty("message").GetString()); Assert.AreEqual(Canary, raw.GetProperty("traceId").GetString()); }
            Assert.DoesNotContain(Canary, row.GetProperty("summary").GetString()!);
            using var detail = await f.SendAsync(route + "/" + row.GetProperty("id").GetString(), key.ApiKey); Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
            using var single = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()); Assert.AreEqual(enabled, single.RootElement.GetProperty("data").TryGetProperty("unsanitized", out _));
            f.SimulateStorageFailure = true;
            using var failure = await f.SendAsync(route, key.ApiKey); Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failure.StatusCode); Assert.DoesNotContain(Canary, await failure.Content.ReadAsStringAsync());
            f.SimulateStorageFailure = false;
        }
        using var info = await f.SendAsync("/api/management-diagnostics/info", key.ApiKey);
        using var body = JsonDocument.Parse(await info.Content.ReadAsStringAsync()); var modes = body.RootElement.GetProperty("data").GetProperty("unsanitizedCapabilities").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.AreEqual((errorRaw ? 1 : 0) + (logRaw ? 1 : 0), modes.Length);
        Assert.AreEqual(errorRaw, modes.Contains(ManagementOperations.ErrorsRead)); Assert.AreEqual(logRaw, modes.Contains(ManagementOperations.LogsRead));
        Assert.AreEqual(modes.Length, f.Logs.Messages.Count(x => x.Contains("sanitization is disabled", StringComparison.Ordinal)));
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(f.ManagementAudits.Rows)); Assert.DoesNotContain(Canary, JsonSerializer.Serialize(f.Audits.Transactions)); Assert.DoesNotContain(Canary, string.Join(" ", f.Logs.Messages));
    }
    [TestMethod, DataRow("Production", "Testing"), DataRow("Testing", "Production"), DataRow("", "Testing"), DataRow("Unknown", "Testing"), DataRow("Testing", "Unknown"), DataRow("Testing", "prod")]
    public async Task ProductionOrUnknownContextRefusesUnsanitizedStartupForEitherModule(string host, string deployment)
    {
        foreach(var module in new[] { "Errors", "Logs" })
            await Assert.ThrowsAsync<Exception>(async () => { await using var f = await SystemErrorApiFixture.CreateAsync(installLogs: true, hostEnvironment: host, configure: values => {
                values["IBeam:Management:Diagnostics:Environment"] = deployment; values[$"IBeam:Management:Diagnostics:{module}:Sanitization:Enabled"] = "false";
            }); });
    }
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task RequestQueryHeaderBodyAndClaimsCannotChangeHostMode(bool raw)
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(installLogs: true, claimedSanitization: raw, configure: values => {
            values["IBeam:Management:Diagnostics:Environment"] = "Testing";
            foreach(var module in new[] { "Errors", "Logs" }) values[$"IBeam:Management:Diagnostics:{module}:Sanitization:Enabled"] = raw ? "false" : "true";
        });
        Seed(f); var key = await f.IssueAsync(ManagementOperations.ErrorsRead, ManagementOperations.LogsRead);
        foreach(var kind in new[] { "system-errors", "system-logs" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/management-diagnostics/{kind}?sanitization.enabled={raw}&includeUnsanitized={!raw}&unsanitized={!raw}");
            request.Headers.Add("X-IBeam-Management-Key", key.ApiKey); request.Headers.Add("X-IBeam-Sanitization", raw.ToString());
            request.Content = JsonContent.Create(new { sanitization = new { enabled = raw } });
            using var response = await f.Client.SendAsync(request); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.AreEqual(raw, body.RootElement.GetProperty("data")[0].TryGetProperty("unsanitized", out _));
        }
    }
    [TestMethod]
    public async Task ModeChangesInvalidateReaderIdsAndCursorsAndDirectProductionProjectionsRefuseRaw()
    {
        var f = new ErrorReaderFixture(); f.Deployment.Environment = "Testing";
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1)) with { Message = Canary }); f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddMinutes(-30)));
        var reader = f.Reader(host: new SanitizationHost()); var first = await reader.ReadAsync(new() { PageSize = 1 });
        f.Options.Sanitization.Enabled = false;
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => reader.GetAsync(first.Items[0].Id));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => reader.ReadAsync(new() { Cursor = first.NextCursor }));
        var projection = new SystemErrorProjection(Options.Create(f.Options), Options.Create(f.Deployment), new SanitizationHost());
        Assert.AreEqual(Canary, projection.Project("id", f.Repository.Rows[0]).Unsanitized?.Message);
        f.Deployment.Environment = "Production";
        Assert.Throws<InvalidOperationException>(() => projection.Project("id", f.Repository.Rows[0]));
        Assert.Throws<InvalidOperationException>(() => new SystemErrorProjection(Options.Create(f.Options), Options.Create(f.Deployment), new SanitizationHost()));
        var log = new LogReaderFixture(); log.Options.Sanitization.Enabled = false;
        Assert.Throws<InvalidOperationException>(() => new SystemLogProjection(Options.Create(log.Options), Options.Create(log.Deployment), new SanitizationHost()));
        log.Deployment.Environment = "Testing";
        log.Options.Sanitization.Enabled = true;
        log.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1)));
        log.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddMinutes(-30)));
        var logs = log.Reader(host: new SanitizationHost());
        var safeLog = await logs.ReadAsync(new() { PageSize = 1 });
        log.Options.Sanitization.Enabled = false;
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => logs.GetAsync(safeLog.Items[0].Id));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => logs.ReadAsync(new() { Cursor = safeLog.NextCursor }));
        Assert.Throws<InvalidOperationException>(() => new SystemLogProjection(Options.Create(log.Options), Options.Create(log.Deployment)));
    }
}
