using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Management;
using IBeam.Management.Models;
using IBeam.Management.Repositories.AzureTable.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class SystemErrorApiIntegrationTests
{
    [TestMethod]
    [TestCategory("StorageIntegration")]
    public async Task RealReadOnlyAzureTableSupportsSafeListAndDetailWithoutChangingRows()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Set IBEAM_MANAGEMENT_TEST_STORAGE to private Azurite.");
        var table = new TableClient(connection, "ErrorApi" + Guid.NewGuid().ToString("N"));
        await table.CreateIfNotExistsAsync();
        try
        {
            var at = ErrorReaderFixture.Now.AddHours(-2);
            var row = new TableEntity("ERR|" + at.ToString("yyyyMMdd"), at.ToString("HHmmssfff") + "|" + Guid.NewGuid().ToString("N"))
            {
                ["OccurredAtUtc"] = at, ["Source"] = "RequestPipeline", ["TraceId"] = SystemErrorApiFixture.Sensitive,
                ["Message"] = "client=" + SystemErrorApiFixture.Sensitive, ["Exception"] = "password=" + SystemErrorApiFixture.Sensitive,
                ["Path"] = "/tenant-data?secret=" + SystemErrorApiFixture.Sensitive, ["Method"] = "POST"
            };
            await table.AddEntityAsync(row);
            var before = (await table.GetEntityAsync<TableEntity>(row.PartitionKey, row.RowKey)).Value.ETag;
            var reader = new AzureTableSystemErrorReadRepository(new TableClient(
                table.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(10))));
            await using var f = await SystemErrorApiFixture.CreateAsync(repository: reader);
            var credential = await f.IssueAsync();
            using var response = await f.SendAsync("/api/management-diagnostics/system-errors", credential.ApiKey);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.IsFalse(body.Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
            Assert.IsFalse(body.Contains(row.RowKey, StringComparison.Ordinal));
            Assert.IsFalse(body.Contains("Exception", StringComparison.OrdinalIgnoreCase));
            using var json = JsonDocument.Parse(body);
            Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
            var id = json.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
            using var detail = await f.SendAsync("/api/management-diagnostics/system-errors/" + id, credential.ApiKey);
            Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
            Assert.IsFalse((await detail.Content.ReadAsStringAsync()).Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
            var after = (await table.GetEntityAsync<TableEntity>(row.PartitionKey, row.RowKey)).Value;
            Assert.AreEqual(before, after.ETag);
            Assert.AreEqual(row.GetString("Message"), after.GetString("Message"));
            Assert.IsFalse(JsonSerializer.Serialize(f.Audits.Transactions).Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        }
        finally { await table.DeleteAsync(); }
    }

    [TestMethod]
    public async Task DirectPublicServiceCallsRequireManagementContextEvenWithoutControllers()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync();
        using var scope = f.App.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemErrorReaderService>();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.ListAsync(new DiagnosticQuery()));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.GetAsync("untrusted-id"));
        Assert.HasCount(0, f.Rows.Queries);
        Assert.AreEqual(0, f.Rows.Gets);
    }

    [TestMethod]
    public async Task SharedSourceEndpointSuppressesAmbiguousLegacyRows()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(configure: values =>
        {
            values["IBeam:Management:Diagnostics:Errors:SourceMode"] = "TaggedShared";
            values["IBeam:Management:Diagnostics:Errors:TrustedTagsConfirmed"] = "true";
        });
        f.Rows.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-3)));
        f.Rows.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-2), "medinsights", "Production"));
        f.Rows.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1), "qurvia", "Production"));
        var credential = await f.IssueAsync();
        using var response = await f.SendAsync("/api/management-diagnostics/system-errors", credential.ApiKey);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
    }
    [TestMethod]
    public async Task OptedInApiFailsStartupWhenItsReadRepositoryIsMissing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SystemErrorApiFixture.CreateAsync(registerRepository: false));
    }
}
