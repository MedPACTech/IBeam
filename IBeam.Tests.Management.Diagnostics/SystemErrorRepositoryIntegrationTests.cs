using System.Collections.Concurrent;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Management.Models;
using IBeam.Management.Repositories.AzureTable.Diagnostics;
using IBeam.Management.Services.Diagnostics;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class SystemErrorRepositoryIntegrationTests
{
    [TestMethod]
    [TestCategory("StorageIntegration")]
    public async Task RealLegacyRowsHaveStableCrossMidnightPaginationAndNoUnboundedQueriesOrWrites()
    {
        var (table, capture) = CreateTable();
        await table.CreateIfNotExistsAsync();
        try
        {
            var times = new[]
            {
                ErrorReaderFixture.Now.AddHours(-25), new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 10, 15, 23, 59, 59, 999, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero),
                ErrorReaderFixture.Now.AddMilliseconds(-1), ErrorReaderFixture.Now
            };
            foreach(var at in times) await table.AddEntityAsync(Row(at));
            var malformedKey = Row(ErrorReaderFixture.Now.AddHours(-1)); malformedKey.RowKey = "not-an-IBeam-error-key";
            await table.AddEntityAsync(malformedKey);
            await table.AddEntityAsync(new TableEntity("OTHER|partition", Guid.NewGuid().ToString("N")) { ["OccurredAtUtc"] = ErrorReaderFixture.Now.AddHours(-2) });
            var before = await Snapshot(table);
            capture.Requests.Clear();
            var f = new ErrorReaderFixture();
            var reader = f.Reader(new AzureTableSystemErrorReadRepository(table));
            var page = await reader.ReadAsync(new DiagnosticQuery { PageSize = 2 });
            var items = page.Items.ToList();
            var guard = 0;
            while(page.NextCursor is not null)
            {
                Assert.IsLessThan(12, ++guard);
                page = await reader.ReadAsync(new DiagnosticQuery { Cursor = page.NextCursor });
                Assert.IsLessThanOrEqualTo(2, page.Items.Count);
                items.AddRange(page.Items);
            }
            Assert.HasCount(4, items);
            Assert.AreEqual(4, items.Select(x => x.Record.RowKey).Distinct().Count());
            var detail = await reader.GetAsync(items[0].Id);
            Assert.AreEqual(items[0].Record.RowKey, detail!.RowKey);
            var repository = new AzureTableSystemErrorReadRepository(table);
            Assert.IsNull(await repository.GetAsync("ERR|20261015", "120000000|" + Guid.NewGuid().ToString("N")));
            Assert.IsTrue(capture.Requests.All(x => x.Method == "GET"));
            var queries = capture.Requests.Where(x => x.Query.Contains("$filter=", StringComparison.Ordinal)).ToArray();
            Assert.IsTrue(queries.Length > 0);
            Assert.IsTrue(queries.All(x => x.Query.Contains("PartitionKey eq 'ERR|", StringComparison.Ordinal) &&
                x.Query.Contains("OccurredAtUtc ge ", StringComparison.Ordinal) && x.Query.Contains("OccurredAtUtc lt ", StringComparison.Ordinal)));
            Assert.IsTrue(capture.Requests.All(x => x.Query.Contains("$select=", StringComparison.Ordinal) &&
                !x.Query.Contains("Message", StringComparison.Ordinal) && !x.Query.Contains("Exception", StringComparison.Ordinal) &&
                !x.Query.Contains("Path", StringComparison.Ordinal)));
            var after = await Snapshot(table);
            CollectionAssert.AreEqual(before, after);
        }
        finally { await table.DeleteAsync(); }
    }

    [TestMethod]
    [TestCategory("StorageIntegration")]
    public async Task SharedTablesSuppressAmbiguousAndMalformedTagsWithoutRewritingHistoricalRows()
    {
        var (table, _) = CreateTable();
        await table.CreateIfNotExistsAsync();
        try
        {
            var rows = Enumerable.Range(1, 7).Select(i => Row(ErrorReaderFixture.Now.AddHours(-2).AddMinutes(i))).ToArray();
            rows[1]["ManagementApplicationKey"] = "qurvia";
            rows[2]["ManagementApplicationKey"] = "bindry"; rows[2]["ManagementEnvironment"] = "Production";
            rows[3]["ManagementApplicationKey"] = "qurvia"; rows[3]["ManagementEnvironment"] = "Test";
            rows[4]["ManagementApplicationKey"] = "qurvia"; rows[4]["ManagementEnvironment"] = "Production";
            rows[5]["ManagementApplicationKey"] = 42; rows[5]["ManagementEnvironment"] = 42;
            rows[6]["ManagementApplicationKey"] = ""; rows[6]["ManagementEnvironment"] = "";
            foreach(var row in rows) await table.AddEntityAsync(row);
            var f = new ErrorReaderFixture();
            f.Options.SourceMode = DiagnosticSourceMode.TaggedShared; f.Options.TrustedTagsConfirmed = true;
            f.Options.DefaultPageSize = 2; f.Options.MaximumPageSize = 2; f.Options.ScanBudgetPerPage = 2;
            var reader = f.Reader(new AzureTableSystemErrorReadRepository(table));
            var page = await reader.ReadAsync(new DiagnosticQuery());
            var found = page.Items.ToList();
            var guard = 0;
            while(page.NextCursor is not null)
            {
                Assert.IsLessThan(12, ++guard);
                page = await reader.ReadAsync(new DiagnosticQuery { Cursor = page.NextCursor });
                Assert.IsLessThanOrEqualTo(2, page.ScannedCount);
                found.AddRange(page.Items);
            }
            Assert.HasCount(1, found);
            Assert.AreEqual(rows[4].RowKey, found[0].Record.RowKey);
            Assert.HasCount(7, await Snapshot(table));
            // Malformed tags must also fail closed in an otherwise confirmed dedicated legacy source.
            var dedicated = new ErrorReaderFixture();
            var dedicatedPage = await dedicated.Reader(new AzureTableSystemErrorReadRepository(table)).ReadAsync(new DiagnosticQuery());
            Assert.HasCount(2, dedicatedPage.Items); // exactly the truly untagged legacy row and matching tagged row
        }
        finally { await table.DeleteAsync(); }
    }

    [TestMethod]
    [TestCategory("StorageIntegration")]
    public async Task MissingStorageIsNeverCreatedAndReadOnlySasCanReadExistingRows()
    {
        var (table, capture) = CreateTable();
        var repository = new AzureTableSystemErrorReadRepository(table);
        await Assert.ThrowsAsync<RequestFailedException>(() => repository.ReadPartitionAsync(
            new SystemErrorPartitionQuery(new DateOnly(2026, 10, 16), ErrorReaderFixture.Now.AddHours(-1), ErrorReaderFixture.Now, 1, null)));
        Assert.IsTrue(capture.Requests.All(x => x.Method == "GET"));
        await table.CreateIfNotExistsAsync();
        try
        {
            var row = Row(ErrorReaderFixture.Now.AddMinutes(-30));
            await table.AddEntityAsync(row);
            var readOnly = new TableClient(table.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(10)));
            var readOnlyRepository = new AzureTableSystemErrorReadRepository(readOnly);
            Assert.AreEqual(repository.SourceBinding, readOnlyRepository.SourceBinding);
            var page = await new ErrorReaderFixture().Reader(readOnlyRepository).ReadAsync(new DiagnosticQuery());
            Assert.HasCount(1, page.Items);
            var denied = await Assert.ThrowsAsync<RequestFailedException>(() => readOnly.UpsertEntityAsync(Row(ErrorReaderFixture.Now.AddMinutes(-10))));
            Assert.AreEqual(403, denied.Status);
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
        return (new TableClient(connection, "Errors" + Guid.NewGuid().ToString("N"), options), capture);
    }
    private static TableEntity Row(DateTimeOffset at)
        => new("ERR|" + at.UtcDateTime.ToString("yyyyMMdd"), at.ToString("HHmmssfff") + "|" + Guid.NewGuid().ToString("N"))
        {
            ["OccurredAtUtc"] = at, ["Source"] = "IBeam.Api", ["TraceId"] = "not-approved-free-text",
            ["Message"] = "client-payload-must-not-be-read", ["Exception"] = "secret-exception-text",
            ["Path"] = "/customer-data?secret=hidden", ["Method"] = "POST"
        };
    private static async Task<string[]> Snapshot(TableClient table)
    {
        var records = new List<string>();
        await foreach(var row in table.QueryAsync<TableEntity>()) records.Add(row.PartitionKey + "/" + row.RowKey + "/" + row.ETag);
        return records.Order(StringComparer.Ordinal).ToArray();
    }
    private sealed class CapturePolicy : HttpPipelineSynchronousPolicy
    {
        public ConcurrentQueue<(string Method, string Query)> Requests { get; } = new();
        public override void OnSendingRequest(HttpMessage message)
            => Requests.Enqueue((message.Request.Method.Method, Uri.UnescapeDataString(message.Request.Uri.ToUri().Query)));
    }
}
