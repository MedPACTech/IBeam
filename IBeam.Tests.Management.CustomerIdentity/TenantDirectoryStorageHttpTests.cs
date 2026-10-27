using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Repositories.AzureTable;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class TenantDirectoryStorageHttpTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task SignedHttpFiltersExistingTenantRowsThroughReadOnlyProviderAndContinuesEmptyPages()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var table = new TableClient(connection, "TenantHttp" + Guid.NewGuid().ToString("N"));
        var first = "10000000-0000-0000-0000-000000000001";
        var second = "20000000-0000-0000-0000-000000000002";
        try
        {
            await table.CreateIfNotExistsAsync();
            await table.AddEntityAsync(new TableEntity("TEN", first) { ["Name"] = "Acme", ["Status"] = "Active", ["ProfileJson"] = "BUSINESS-CANARY" });
            await table.AddEntityAsync(new TableEntity("TEN", second) { ["Name"] = "Beacon", ["Status"] = "Disabled", ["ProfileJson"] = "BUSINESS-CANARY" });
            var readOnly = new TableClient(table.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(10)));
            await using var f = await DelegationFixture.Start(new AzureTableManagementCustomerTenantReadRepository(readOnly));
            var path = "/api/management-identity/tenants?pageSize=1&status=Disabled&namePrefix=bea";
            using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: path), path: path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual(0, page.RootElement.GetProperty("data").GetArrayLength());
            var cursor = page.RootElement.GetProperty("continuationToken").GetString();
            Assert.IsNotNull(cursor);
            var next = path + "&continuationToken=" + cursor;
            using var continued = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: next), path: next);
            Assert.AreEqual(HttpStatusCode.OK, continued.StatusCode);
            var text = await continued.Content.ReadAsStringAsync();
            using var found = JsonDocument.Parse(text);
            Assert.AreEqual(second, found.RootElement.GetProperty("data")[0].GetProperty("tenantId").GetString());
            Assert.DoesNotContain("BUSINESS-CANARY", text);
            Assert.DoesNotContain("BUSINESS-CANARY", JsonSerializer.Serialize(f.Audit.Rows));
        }
        finally { await table.DeleteAsync(); }
    }
}
