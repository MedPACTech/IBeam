using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Management;
using IBeam.Management.Models;
using IBeam.Management.Repositories.AzureTable.Diagnostics;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class SystemLogRepositoryIntegrationTests
{
    private const string Canary = SystemErrorApiFixture.Sensitive;
    private static readonly string[] ExcludedColumns =
    [
        "Message", "Detail", "BeforeJson", "AfterJson", "OriginalJson", "TransformedJson", "ActorId", "IpAddress",
        "UserAgent", "DeviceId", "TenantId", "EntityId", "EntityName", "ServiceName", "QuerySignature", "CorrelationId"
    ];

    [TestMethod]
    [TestCategory("StorageIntegration")]
    public async Task OnlyOperationalSystemRowsAreReadAndExcludedColumnsAreNeverSelected()
    {
        var (table, capture) = CreateTable();
        await table.CreateIfNotExistsAsync();
        try
        {
            var at = LogReaderFixture.Now.AddHours(-2);
            var system = Row(at, "System");
            await table.AddEntityAsync(system);
            // What AzureTableSystemLogSink writes for audit traffic: tenant-less audit lands in the SYSTEM partition too.
            await table.AddEntityAsync(Row(at.AddMinutes(1), "EntityChange"));
            await table.AddEntityAsync(Row(at.AddMinutes(2), "SelectRollup", rowKey: "ROLLUP|" + new string('A', 64), rollup: true));
            var tenant = Row(at.AddMinutes(3), "System");
            tenant.PartitionKey = "TENANT|" + Guid.NewGuid().ToString("D") + "|DAY|" + at.ToString("yyyyMMdd");
            await table.AddEntityAsync(tenant);
            var badKey = Row(at.AddMinutes(4), "System", rowKey: "not-an-event-key");
            await table.AddEntityAsync(badKey);
            var before = await Snapshot(table);
            var entityChangeKey = await FindKey(table, "EntityChange");
            capture.Requests.Clear();
            var f = new LogReaderFixture();
            var reader = f.Reader(new AzureTableSystemLogReadRepository(table));
            var page = await reader.ReadAsync(new SystemLogQuery());
            var items = page.Items.ToList();
            while (page.NextCursor is not null)
            {
                page = await reader.ReadAsync(new SystemLogQuery { Cursor = page.NextCursor });
                items.AddRange(page.Items);
            }
            Assert.HasCount(1, items);
            Assert.AreEqual(system.RowKey, items[0].Record.RowKey);
            var dto = f.Projection().Project(items[0].Id, items[0].Record);
            var detail = await reader.GetAsync(items[0].Id);
            Assert.AreEqual(system.RowKey, detail!.RowKey);
            var repository = new AzureTableSystemLogReadRepository(table);
            Assert.IsNull(await repository.GetAsync(system.PartitionKey, entityChangeKey));
            var serialized = JsonSerializer.Serialize(new object[] { dto, items, page });
            Assert.IsFalse(serialized.Contains(Canary, StringComparison.Ordinal));
            Assert.IsTrue(capture.Requests.All(x => x.Method == "GET"));
            var queries = capture.Requests.Where(x => x.Query.Contains("$filter=", StringComparison.Ordinal)).ToArray();
            Assert.IsNotEmpty(queries);
            Assert.IsTrue(queries.All(x => x.Query.Contains("PartitionKey eq 'SYSTEM|DAY|", StringComparison.Ordinal) &&
                x.Query.Contains("Category eq 'System'", StringComparison.Ordinal) &&
                x.Query.Contains("OccurredAtUtc ge ", StringComparison.Ordinal) && x.Query.Contains("OccurredAtUtc lt ", StringComparison.Ordinal)));
            Assert.IsTrue(capture.Requests.All(x => x.Query.Contains("$select=", StringComparison.Ordinal) &&
                ExcludedColumns.All(c => !SelectedColumns(x.Query).Contains(c))));
            CollectionAssert.AreEqual(before, await Snapshot(table));
        }
        finally { await table.DeleteAsync(); }
    }

    [TestMethod]
    [TestCategory("StorageIntegration")]
    public async Task FiltersArePushedToStorageWithEscapingAndPaginationIsStable()
    {
        var (table, capture) = CreateTable();
        await table.CreateIfNotExistsAsync();
        try
        {
            var times = new[]
            {
                new DateTimeOffset(2026, 10, 15, 23, 59, 59, 999, TimeSpan.Zero), new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero),
                LogReaderFixture.Now.AddHours(-3), LogReaderFixture.Now.AddHours(-2), LogReaderFixture.Now.AddMilliseconds(-1)
            };
            foreach (var at in times) await table.AddEntityAsync(Row(at, "System", trace: "wanted:1"));
            await table.AddEntityAsync(Row(LogReaderFixture.Now.AddHours(-1), "System", trace: "other"));
            var warning = Row(LogReaderFixture.Now.AddHours(-1).AddMinutes(1), "System", trace: "wanted:1");
            warning["Level"] = "Warning";
            await table.AddEntityAsync(warning);
            capture.Requests.Clear();
            var f = new LogReaderFixture();
            f.Options.DefaultPageSize = 2;
            var reader = f.Reader(new AzureTableSystemLogReadRepository(table));
            var page = await reader.ReadAsync(new SystemLogQuery { TraceId = "wanted:1", Level = "Error" });
            var items = page.Items.ToList();
            var guard = 0;
            while (page.NextCursor is not null)
            {
                Assert.IsLessThan(12, ++guard);
                page = await reader.ReadAsync(new SystemLogQuery { Cursor = page.NextCursor });
                Assert.IsLessThanOrEqualTo(2, page.Items.Count);
                items.AddRange(page.Items);
            }
            Assert.HasCount(5, items);
            Assert.AreEqual(5, items.Select(x => x.Record.RowKey).Distinct().Count());
            Assert.IsTrue(capture.Requests.Where(x => x.Query.Contains("$filter=", StringComparison.Ordinal))
                .All(x => x.Query.Contains("TraceId eq 'wanted:1'", StringComparison.Ordinal) && x.Query.Contains("Level eq 'Error'", StringComparison.Ordinal)));
            // The trace pattern forbids quotes, but the repository still escapes if called directly.
            var direct = await new AzureTableSystemLogReadRepository(table).ReadPartitionAsync(new SystemLogPartitionQuery(
                new DateOnly(2026, 10, 16), LogReaderFixture.Now.AddDays(-1), LogReaderFixture.Now, 10, null, TraceId: "x' or 'a' eq 'a"));
            Assert.HasCount(0, direct.Records);
        }
        finally { await table.DeleteAsync(); }
    }

    [TestMethod]
    [TestCategory("StorageIntegration")]
    public async Task MissingStorageIsNeverCreatedAndReadOnlySasServesTheHttpRoutesWithoutLeaks()
    {
        var (table, capture) = CreateTable();
        var repository = new AzureTableSystemLogReadRepository(table);
        await Assert.ThrowsAsync<RequestFailedException>(() => repository.ReadPartitionAsync(
            new SystemLogPartitionQuery(new DateOnly(2026, 10, 16), LogReaderFixture.Now.AddHours(-1), LogReaderFixture.Now, 1, null)));
        Assert.IsTrue(capture.Requests.All(x => x.Method == "GET"));
        await table.CreateIfNotExistsAsync();
        try
        {
            var row = Row(LogReaderFixture.Now.AddMinutes(-30), "System");
            await table.AddEntityAsync(row);
            await table.AddEntityAsync(Row(LogReaderFixture.Now.AddMinutes(-20), "EntityChange"));
            var etag = (await table.GetEntityAsync<TableEntity>(row.PartitionKey, row.RowKey)).Value.ETag;
            var readOnly = new TableClient(table.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(10)));
            var denied = await Assert.ThrowsAsync<RequestFailedException>(() => readOnly.UpsertEntityAsync(Row(LogReaderFixture.Now, "System")));
            Assert.AreEqual(403, denied.Status);
            var readOnlyRepository = new AzureTableSystemLogReadRepository(readOnly);
            Assert.AreEqual(repository.SourceBinding, readOnlyRepository.SourceBinding);
            await using var f = await SystemErrorApiFixture.CreateAsync(installed: false, installLogs: true, logRepository: readOnlyRepository);
            var credential = await f.IssueAsync(ManagementOperations.LogsRead);
            using var response = await f.SendAsync("/api/management-diagnostics/system-logs", credential.ApiKey);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.IsFalse(body.Contains(Canary, StringComparison.Ordinal));
            Assert.IsFalse(body.Contains(row.RowKey, StringComparison.Ordinal));
            using var json = JsonDocument.Parse(body);
            Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
            var id = json.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
            using var detail = await f.SendAsync("/api/management-diagnostics/system-logs/" + id, credential.ApiKey);
            Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
            Assert.IsFalse((await detail.Content.ReadAsStringAsync()).Contains(Canary, StringComparison.Ordinal));
            Assert.AreEqual(etag, (await table.GetEntityAsync<TableEntity>(row.PartitionKey, row.RowKey)).Value.ETag);
            Assert.IsFalse(JsonSerializer.Serialize(f.Audits.Transactions).Contains(Canary, StringComparison.Ordinal));
            Assert.IsFalse(string.Join(" ", f.Logs.Messages).Contains(Canary, StringComparison.Ordinal));
        }
        finally { await table.DeleteAsync(); }
    }

    private static (TableClient Table, CapturePolicy Capture) CreateTable()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Set IBEAM_MANAGEMENT_TEST_STORAGE to private Azurite.");
        var capture = new CapturePolicy();
        var options = new TableClientOptions(); options.Retry.MaxRetries = 0;
        options.AddPolicy(capture, HttpPipelinePosition.PerCall);
        return (new TableClient(connection, "Logs" + Guid.NewGuid().ToString("N"), options), capture);
    }

    /// <summary>Mirrors AzureTableSystemLogSink's columns, with a canary in every column the reader must exclude.</summary>
    private static TableEntity Row(DateTimeOffset at, string category, string? rowKey = null, bool rollup = false, string trace = "trace-1")
        => new("SYSTEM|DAY|" + at.UtcDateTime.ToString("yyyyMMdd"), rowKey ?? at.UtcDateTime.ToString("HHmmssfffffff") + "|" + Guid.NewGuid().ToString("N"))
        {
            ["OccurredAtUtc"] = at, ["DateUtc"] = at.UtcDateTime.ToString("yyyy-MM-dd"), ["Category"] = category,
            ["Source"] = "RequestPipeline", ["Level"] = "Error", ["TraceId"] = trace, ["IsSelectRollup"] = rollup,
            ["Message"] = "message " + Canary, ["Detail"] = "System.Exception: " + Canary,
            ["BeforeJson"] = "{\"ssn\":\"" + Canary + "\"}", ["AfterJson"] = "{\"ssn\":\"" + Canary + "\"}",
            ["OriginalJson"] = "{\"q\":\"" + Canary + "\"}", ["TransformedJson"] = "{\"q\":\"" + Canary + "\"}",
            ["ActorId"] = "actor-" + Canary, ["IpAddress"] = "203.0.113.7-" + Canary, ["UserAgent"] = "ua-" + Canary,
            ["DeviceId"] = "device-" + Canary, ["TenantId"] = Guid.NewGuid(), ["EntityId"] = Guid.NewGuid(),
            ["EntityName"] = "Patient-" + Canary, ["ServiceName"] = "svc-" + Canary, ["QuerySignature"] = "sig-" + Canary,
            ["CorrelationId"] = "corr-" + Canary
        };
    private static async Task<string> FindKey(TableClient table, string category)
    {
        await foreach (var row in table.QueryAsync<TableEntity>($"Category eq '{category}'")) return row.RowKey;
        throw new InvalidOperationException("Fixture row is missing.");
    }
    private static string[] SelectedColumns(string query)
    {
        var start = query.IndexOf("$select=", StringComparison.Ordinal);
        if (start < 0) return [];
        var value = query[(start + 8)..];
        var end = value.IndexOf('&');
        return (end < 0 ? value : value[..end]).Split(',');
    }
    private static async Task<string[]> Snapshot(TableClient table)
    {
        var records = new List<string>();
        await foreach (var row in table.QueryAsync<TableEntity>()) records.Add(row.PartitionKey + "/" + row.RowKey + "/" + row.ETag);
        return records.Order(StringComparer.Ordinal).ToArray();
    }
    private sealed class CapturePolicy : HttpPipelineSynchronousPolicy
    {
        public ConcurrentQueue<(string Method, string Query)> Requests { get; } = new();
        public override void OnSendingRequest(HttpMessage message)
            => Requests.Enqueue((message.Request.Method.Method, Uri.UnescapeDataString(message.Request.Uri.ToUri().Query)));
    }
}
