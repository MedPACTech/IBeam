using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Repositories.AzureTable;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class TenantAdministrationStorageTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RealIdentityWritesRecoverExtensionsEnforceReferenceAndVersionAndPreserveLegacyConsumerColumns()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var suffix = Guid.NewGuid().ToString("N");
        var tenants = new TableClient(connection, "AdminTenants" + suffix); var metadata = new TableClient(connection, "AdminMetadata" + suffix);
        var operations = new TableClient(connection, "AdminOperations" + suffix); var audit = new TableClient(connection, "AdminAudit" + suffix);
        try {
            await tenants.CreateIfNotExistsAsync(); await metadata.CreateIfNotExistsAsync(); await operations.CreateIfNotExistsAsync(); await audit.CreateIfNotExistsAsync();
            var store = new AzureTableIdentityTenantStore(new TableServiceClient(connection), Options.Create(new AzureTableIdentityOptions {
                TenantsTableName = tenants.Name, CreateTablesIfNotExists = false }));
            var legacy = Guid.NewGuid();
            await tenants.AddEntityAsync(new TableEntity("TEN", legacy.ToString("D")) { ["Name"] = "Legacy", ["Status"] = "active", ["ConsumerProfileJson"] = "PRIVATE-PROFILE-CANARY" });
            var before = (await tenants.GetEntityAsync<TableEntity>("TEN", legacy.ToString("D"))).Value;
            var versioned = (await store.FindVersionedAsync(legacy))!; Assert.IsNull(versioned.Tenant.CreatedAt);
            Assert.AreEqual(before.ETag.ToString(), versioned.Version); Assert.IsNull(versioned.LastOperationId);
            Assert.AreEqual(before.ETag, (await tenants.GetEntityAsync<TableEntity>("TEN", legacy.ToString("D"))).Value.ETag);
            var administration = new AzureTableManagementTenantAdministrationRepository(metadata);
            await using var f = await DelegationFixture.Start(operations: new AzureTableManagementIdentityOperationRepository(operations),
                operationAudit: new AzureTableManagementIdentityOperationAuditRepository(audit), tenantStore: store, tenantMetadata: administration);
            f.TenantHooks.FailCreated = true;
            const string path = "/api/management-identity/tenants";
            var body = Encoding.UTF8.GetBytes("{\"name\":\"Enterprise\",\"externalReference\":\"CRM:123\"}");
            using var failed = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", path, body), "POST", path, body);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode, await failed.Content.ReadAsStringAsync());
            var all = new List<TableEntity>(); await foreach (var row in tenants.QueryAsync<TableEntity>()) all.Add(row);
            Assert.HasCount(2, all); var reserved = all.Single(x => x.RowKey != legacy.ToString("D")); var initialEtag = reserved.ETag;
            f.TenantHooks.FailCreated = false;
            using var created = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", path, body), "POST", path, body);
            var text = await created.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, text);
            using var json = JsonDocument.Parse(text); var data = json.RootElement.GetProperty("data");
            var id = data.GetProperty("tenant").GetProperty("tenantId").GetGuid();
            var version = data.GetProperty("tenant").GetProperty("version").GetString()!;
            Assert.AreEqual(reserved.RowKey, id.ToString("D")); Assert.AreEqual(initialEtag, (await tenants.GetEntityAsync<TableEntity>("TEN", id.ToString("D"))).Value.ETag);
            Assert.IsNull(f.TenantHooks.Context!.AuthUserId);
            var duplicate = Encoding.UTF8.GetBytes("{\"name\":\"Another\",\"externalReference\":\"crm:123\"}");
            using var conflict = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", path, duplicate,
                idempotencyKey: "duplicate-reference-intent"), "POST", path, duplicate, idempotencyKey: "duplicate-reference-intent");
            Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
            var rename = Encoding.UTF8.GetBytes("{\"name\":\"Renamed Legacy\"}"); var renamePath = path + "/" + legacy;
            using var changed = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsUpdate, "PATCH", renamePath, rename, ifMatch: versioned.Version),
                "PATCH", renamePath, rename, ifMatch: versioned.Version);
            var changedText = await changed.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode, changedText);
            using var changedJson = JsonDocument.Parse(changedText); Assert.AreEqual("unknown", changedJson.RootElement.GetProperty("data").GetProperty("source").GetString());
            Assert.AreEqual("Active", changedJson.RootElement.GetProperty("data").GetProperty("tenant").GetProperty("status").GetString());
            Assert.DoesNotContain("PRIVATE-PROFILE", changedText);
            var after = (await tenants.GetEntityAsync<TableEntity>("TEN", legacy.ToString("D"))).Value;
            Assert.AreEqual("PRIVATE-PROFILE-CANARY", after.GetString("ConsumerProfileJson")); Assert.AreEqual("Renamed Legacy", after.GetString("Name"));
            Assert.AreEqual("active", after.GetString("Status"));
            Assert.IsFalse(after.ContainsKey("CreatedAt")); Assert.IsTrue(after.ContainsKey("LastOperationId"));
            var scope = new ManagementTenantAdministrationScope(DiscoveryProtocolTests.Info.IdentityRealmId, "atlas", "Testing");
            var oldMeta = (await administration.GetAsync(scope, id))!;
            await Assert.ThrowsAsync<InvalidOperationException>(() => administration.ReplaceAsync(oldMeta with { ExternalReference = "CRM:changed" }, oldMeta.Version));
            Assert.IsNull(await administration.GetAsync(scope with { IdentityRealmId = Guid.NewGuid() }, id));
            await foreach (var row in operations.QueryAsync<TableEntity>()) {
                var stored = JsonSerializer.Serialize(row); Assert.DoesNotContain("Enterprise", stored); Assert.DoesNotContain("CRM:123", stored); Assert.DoesNotContain("PRIVATE-PROFILE", stored);
            }
            await foreach (var row in metadata.QueryAsync<TableEntity>()) Assert.DoesNotContain("Enterprise", JsonSerializer.Serialize(row));
        }
        finally { await tenants.DeleteAsync(); await metadata.DeleteAsync(); await operations.DeleteAsync(); await audit.DeleteAsync(); }
    }
}
