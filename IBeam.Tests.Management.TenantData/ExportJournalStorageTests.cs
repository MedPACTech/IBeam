using Azure;
using Azure.Data.Tables;
using IBeam.Management;
using IBeam.Management.Operations;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.TenantData;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class ExportJournalStorageTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task NativeJournalUsesFullDeploymentScopeExactEtagsAndSeparateProtectedRecoveryWithoutRuntimeSchemaCreation()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("A private loopback Azurite fixture is required.");
        var endpoint = System.Text.RegularExpressions.Regex.Match(connection, "(?:^|;)TableEndpoint=([^;]+)");
        if (!endpoint.Success || !Uri.TryCreate(endpoint.Groups[1].Value, UriKind.Absolute, out var address) || !address.IsLoopback || address.Scheme != "http")
            Assert.Fail("The export storage test accepts only private loopback fixtures.");
        var table = new TableClient(connection, "Export" + Guid.NewGuid().ToString("N")); var store = new AzureTableManagementTenantExportJobRepository(table);
        var f = new DurableExportTests.Fixture(); await f.Submit(); var source = (await f.Store.GetAsync(f.Execution))!;
        try
        {
            Assert.IsNull(await store.GetAsync(f.Execution));
            await Assert.ThrowsAsync<RequestFailedException>(() => table.AddEntityAsync(new TableEntity("missing", "table")));
            await table.CreateIfNotExistsAsync();
            Assert.IsTrue(await store.TryCreateAsync(source with { Version = "", ProtectedArtifact = "protected-fixture-ciphertext" }));
            Assert.IsFalse(await store.TryCreateAsync(source));
            var read = (await store.GetAsync(f.Execution))!;
            Assert.IsFalse(string.IsNullOrEmpty(read.Version)); Assert.AreEqual("protected-fixture-ciphertext", read.ProtectedArtifact);
            Assert.AreEqual(source.Definition.InventoryVersion, read.Definition.InventoryVersion);
            Assert.DoesNotContain("protected-fixture-ciphertext", System.Text.Json.JsonSerializer.Serialize(read));
            var claimed = await store.ReplaceAsync(read with { LeaseId = Guid.NewGuid(), LeaseExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(30) }, read.Version);
            Assert.AreNotEqual(read.Version, claimed.Version);
            await Assert.ThrowsAsync<ManagementOperationConflictException>(() => store.ReplaceAsync(read with { CancelRequested = true }, read.Version));
            await Assert.ThrowsAsync<ManagementOperationConflictException>(() => store.ReplaceAsync(claimed, " * "));
            Assert.IsNull(await store.GetAsync(f.Execution with { Environment = "Other" }));
            Assert.IsNull(await store.GetAsync(f.Execution with { Authority = f.Execution.Authority with { ApplicationId = Guid.NewGuid() } }));
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => store.GetAsync(f.Execution with { Authority = f.Execution.Authority with { ActorUserId = Guid.NewGuid() } }));
            var d = new ManagementTenantExportDeployment(f.Execution.ApplicationKey, f.Execution.Environment, f.Execution.Authority.OrganizationId, f.Execution.Authority.ApplicationId, f.Execution.Authority.RealmId);
            var page = await store.ListPageAsync(d, 1, null); Assert.HasCount(1, page.Items); Assert.AreEqual(f.Execution.JobId, page.Items[0].JobId);
            Assert.HasCount(0, (await store.ListPageAsync(d with { Environment = "Other" }, 1, null)).Items);
            Assert.HasCount(1, (await store.ListPageAsync(d with { OrganizationId = Guid.Empty, ApplicationId = Guid.Empty }, 1, null)).Items);
            await foreach (var raw in table.QueryAsync<TableEntity>())
            {
                Assert.DoesNotContain("protected-fixture-ciphertext", raw.GetString("Metadata"));
                Assert.AreEqual("protected-fixture-ciphertext", raw.GetString("ProtectedArtifact"));
            }
        }
        finally { try { await table.DeleteAsync(); } catch (RequestFailedException ex) when (ex.Status == 404) { } }
    }
}
