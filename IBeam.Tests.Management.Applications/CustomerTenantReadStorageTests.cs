using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Repositories.AzureTable;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class CustomerTenantReadStorageTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ExistingTenantRowsReadThroughReadOnlySasWithoutProfilesOrSchemaWrites()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var table = new TableClient(connection, "CustomerTenants" + Guid.NewGuid().ToString("N"));
        var first = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("20000000-0000-0000-0000-000000000002");
        try
        {
            var repository = new AzureTableManagementCustomerTenantReadRepository(table);
            await Assert.ThrowsAsync<RequestFailedException>(() => repository.ReadPageAsync(1, null));
            await table.CreateIfNotExistsAsync();
            foreach (var id in new[] { first, second })
                await table.AddEntityAsync(new TableEntity("TEN", id.ToString("D")) {
                    ["Name"] = id == first ? "Acme Enterprise" : "Beacon Enterprise", ["Status"] = id == first ? "Active" : "Disabled",
                    ["CreatedAt"] = DateTimeOffset.UtcNow, ["ProfileJson"] = "BUSINESS-PAYLOAD-CANARY", ["PrivateToken"] = "SECRET-CANARY" });
            await table.AddEntityAsync(new TableEntity("OTHER", Guid.NewGuid().ToString("D")) { ["Name"] = "CROSS-PARTITION-CANARY" });
            var before = (await table.GetEntityAsync<TableEntity>("TEN", first.ToString("D"))).Value.ETag;
            var readOnly = new TableClient(table.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(10)));
            var reader = new AzureTableManagementCustomerTenantReadRepository(readOnly);
            Assert.AreEqual(repository.SourceId, reader.SourceId);
            var page = await reader.ReadPageAsync(1, null);
            Assert.HasCount(1, page.Items); Assert.IsNotNull(page.ContinuationToken);
            Assert.AreEqual(first.ToString("D"), page.Items[0].RowKey);
            var next = await reader.ReadPageAsync(1, page.ContinuationToken);
            Assert.HasCount(1, next.Items);
            Assert.AreEqual(second.ToString("D"), next.Items[0].RowKey);
            var detail = await reader.FindAsync(first); Assert.IsNotNull(detail);
            Assert.AreEqual("Acme Enterprise", detail.Name);
            Assert.IsNull(await reader.FindAsync(Guid.NewGuid()));
            var text = JsonSerializer.Serialize(page);
            Assert.DoesNotContain("BUSINESS-PAYLOAD-CANARY", text); Assert.DoesNotContain("SECRET-CANARY", text);
            Assert.DoesNotContain("CROSS-PARTITION-CANARY", text);
            Assert.DoesNotContain(page.ContinuationToken!, text);
            Assert.AreEqual(before, (await table.GetEntityAsync<TableEntity>("TEN", first.ToString("D"))).Value.ETag);
            await Assert.ThrowsAsync<RequestFailedException>(() => readOnly.AddEntityAsync(new TableEntity("TEN", Guid.NewGuid().ToString("D"))));
        }
        finally { await table.DeleteAsync(); }
    }
}
