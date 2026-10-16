using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Management;
using IBeam.Management.Repositories.AzureTable.Diagnostics;
namespace IBeam.Tests.Management.Diagnostics;
[TestClass]
public sealed class DiagnosticIsolationIntegrationTests
{
    private const string Canary = "mixed-source-client-secret-9b480c";
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task SharedReadOnlySourcesIsolateApplicationsAndSuppressHistoricalBusinessAndRawFields()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var errors = new TableClient(connection, "MixedErrors" + Guid.NewGuid().ToString("N"));
        var logs = new TableClient(connection, "MixedLogs" + Guid.NewGuid().ToString("N"));
        var other = new TableClient(connection, "OtherErrors" + Guid.NewGuid().ToString("N"));
        await errors.CreateIfNotExistsAsync(); await logs.CreateIfNotExistsAsync(); await other.CreateIfNotExistsAsync();
        try
        {
            // Historical, partial, wrong-typed, wrong-environment and foreign-app tags must never be inferred from message/URL/tenant.
            var bindings = new (object? App, object? Env)[] { (null, null), ("qurvia", null), (42, "Production"), ("qurvia", "Testing"),
                ("qurvia", "Production"), ("bindry", "Production"), ("qurvia", "Production") };
            for (var i = 0; i < bindings.Length; i++)
            {
                var at = ErrorReaderFixture.Now.AddHours(-2).AddMinutes(i);
                await errors.AddEntityAsync(Row(at, false, bindings[i].App, bindings[i].Env));
                await logs.AddEntityAsync(Row(at, true, bindings[i].App, bindings[i].Env));
            }
            foreach (var category in new[] { "EntityChange", "SelectRollup" })
            {
                var row = Row(ErrorReaderFixture.Now.AddMinutes(-30), true, "qurvia", "Production");
                row["Category"] = category; row["IsSelectRollup"] = category == "SelectRollup"; await logs.AddEntityAsync(row);
            }
            var customer = Row(ErrorReaderFixture.Now.AddMinutes(-20), true, "qurvia", "Production");
            customer.PartitionKey = "TENANT|" + Guid.NewGuid() + "|DAY|" + ErrorReaderFixture.Now.ToString("yyyyMMdd"); await logs.AddEntityAsync(customer);
            var beforeErrors = await Snapshot(errors); var beforeLogs = await Snapshot(logs);
            var roErrors = ReadOnly(errors); var roLogs = ReadOnly(logs);
            var denied = await Assert.ThrowsAsync<RequestFailedException>(() => roErrors.UpsertEntityAsync(Row(ErrorReaderFixture.Now, false, "qurvia", "Production")));
            Assert.AreEqual(403, denied.Status);
            var firstRaw = Row(ErrorReaderFixture.Now, true, "qurvia", "Production");
            denied = await Assert.ThrowsAsync<RequestFailedException>(() => roLogs.DeleteEntityAsync(firstRaw.PartitionKey, firstRaw.RowKey)); Assert.AreEqual(403, denied.Status);
            await using var q = await Target("qurvia", roErrors, roLogs);
            await using var b = await Target("bindry", roErrors, roLogs);
            await using var moved = await Target("qurvia", ReadOnly(other), roLogs);
            var qkey = await q.IssueAsync(ManagementOperations.ErrorsRead, ManagementOperations.LogsRead);
            var bkey = await b.IssueAsync(ManagementOperations.ErrorsRead, ManagementOperations.LogsRead);
            var movedKey = await moved.IssueAsync(ManagementOperations.ErrorsRead);
            foreach (var kind in new[] { "system-errors", "system-logs" })
            {
                var route = "/api/management-diagnostics/" + kind;
                using var response = await q.SendAsync(route + "?pageSize=1&tenantId=" + Guid.NewGuid() + "&applicationKey=bindry&environment=Testing&category=EntityChange", qkey.ApiKey);
                var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
                using var json = JsonDocument.Parse(text); var data = json.RootElement.GetProperty("data"); Assert.AreEqual(1, data.GetArrayLength());
                var id = data[0].GetProperty("id").GetString()!; var cursor = json.RootElement.GetProperty("continuationToken").GetString(); Assert.IsNotNull(cursor);
                Assert.DoesNotContain(Canary, text);
                using var detail = await q.SendAsync(route + "/" + id, qkey.ApiKey); Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
                Assert.DoesNotContain(Canary, await detail.Content.ReadAsStringAsync());
                Assert.AreEqual(HttpStatusCode.NotFound, (await b.SendAsync(route + "/" + id, bkey.ApiKey)).StatusCode);
                Assert.AreEqual(HttpStatusCode.BadRequest, (await b.SendAsync(route + "?continuationToken=" + cursor, bkey.ApiKey)).StatusCode);
                Assert.AreEqual(HttpStatusCode.BadRequest, (await q.SendAsync(route + "?continuationToken=" + cursor + "&pageSize=2", qkey.ApiKey)).StatusCode);
                if (kind == "system-errors") Assert.AreEqual(HttpStatusCode.NotFound, (await moved.SendAsync(route + "/" + id, movedKey.ApiKey)).StatusCode);
                var count = 1;
                while (cursor is not null)
                {
                    using var next = await q.SendAsync(route + "?continuationToken=" + cursor, qkey.ApiKey);
                    Assert.AreEqual(HttpStatusCode.OK, next.StatusCode); var raw = await next.Content.ReadAsStringAsync(); Assert.DoesNotContain(Canary, raw);
                    using var page = JsonDocument.Parse(raw); count += page.RootElement.GetProperty("data").GetArrayLength(); cursor = page.RootElement.GetProperty("continuationToken").GetString();
                }
                Assert.AreEqual(2, count);
                using var foreign = await b.SendAsync(route, bkey.ApiKey); using var foreignBody = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
                Assert.AreEqual(1, foreignBody.RootElement.GetProperty("data").GetArrayLength());
                q.SimulateStorageFailure = true;
                using var failure = await q.SendAsync(route, qkey.ApiKey); Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
                Assert.DoesNotContain(Canary, await failure.Content.ReadAsStringAsync()); Assert.DoesNotContain(SystemErrorApiFixture.Sensitive, await failure.Content.ReadAsStringAsync());
                q.SimulateStorageFailure = false;
            }
            foreach (var fixture in new[] { q, b, moved })
            {
                var serialized = JsonSerializer.Serialize(fixture.ManagementAudits.Rows);
                Assert.DoesNotContain(Canary, serialized); Assert.DoesNotContain(SystemErrorApiFixture.Sensitive, serialized);
                Assert.DoesNotContain(qkey.ApiKey, serialized); Assert.DoesNotContain(bkey.ApiKey, serialized);
                Assert.DoesNotContain(Canary, string.Join(" ", fixture.Logs.Messages));
            }
            CollectionAssert.AreEqual(beforeErrors, await Snapshot(errors)); CollectionAssert.AreEqual(beforeLogs, await Snapshot(logs));
        }
        finally { await errors.DeleteAsync(); await logs.DeleteAsync(); await other.DeleteAsync(); }
    }
    private static TableClient ReadOnly(TableClient table) => new(table.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(15)));
    private static Task<SystemErrorApiFixture> Target(string app, TableClient errors, TableClient logs)
        => SystemErrorApiFixture.CreateAsync(repository: new AzureTableSystemErrorReadRepository(errors), installLogs: true,
            logRepository: new AzureTableSystemLogReadRepository(logs), managementAudit: true, configure: values => {
                values["IBeam:Management:Diagnostics:ApplicationKey"] = app;
                foreach (var kind in new[] { "Errors", "Logs" }) {
                    values[$"IBeam:Management:Diagnostics:{kind}:SourceMode"] = "TaggedShared";
                    values[$"IBeam:Management:Diagnostics:{kind}:TrustedTagsConfirmed"] = "true";
                }
            });
    private static TableEntity Row(DateTimeOffset at, bool log, object? app, object? env)
    {
        var row = new TableEntity((log ? "SYSTEM|DAY|" : "ERR|") + at.ToString("yyyyMMdd"), at.ToString(log ? "HHmmssfffffff" : "HHmmssfff") + "|" + Guid.NewGuid().ToString("N")) {
            ["OccurredAtUtc"] = at, ["Source"] = Canary, ["TraceId"] = Canary, ["Level"] = "Error", ["Category"] = "System", ["IsSelectRollup"] = false };
        foreach (var name in new[] { "Message", "Exception", "ExceptionMessage", "StackTrace", "Path", "Method", "RequestBody", "ResponseBody", "Detail", "OriginalJson", "TransformedJson", "BeforeJson", "AfterJson", "ActorId", "UserAgent", "DeviceId", "IpAddress", "EntityName", "ServiceName", "QuerySignature", "CorrelationId" }) row[name] = Canary;
        row["TenantId"] = Guid.NewGuid(); row["EntityId"] = Guid.NewGuid();
        if (app is not null) row["ManagementApplicationKey"] = app; if (env is not null) row["ManagementEnvironment"] = env; return row;
    }
    private static async Task<string[]> Snapshot(TableClient table)
    { var rows = new List<string>(); await foreach (var row in table.QueryAsync<TableEntity>()) rows.Add(row.PartitionKey + "/" + row.RowKey + "/" + row.ETag); return rows.Order(StringComparer.Ordinal).ToArray(); }
}
