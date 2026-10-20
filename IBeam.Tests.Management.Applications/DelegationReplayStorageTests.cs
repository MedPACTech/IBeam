using Azure.Data.Tables;
using IBeam.Management;
using IBeam.Management.Repositories.AzureTable;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class DelegationReplayStorageTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ReplayClaimsAreAtomicAcrossInstancesAndCleanupCannotRemoveLiveAssertions()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var table = new TableClient(connection, "ActorReplay" + Guid.NewGuid().ToString("N"));
        var realm = Guid.NewGuid(); var assertion = Guid.NewGuid();
        var first = new AzureTableManagementDelegationReplayStore(table);
        var second = new AzureTableManagementDelegationReplayStore(table);
        try
        {
            await Assert.ThrowsAsync<ManagementConnectionUnavailableException>(() => first.TryClaimAsync(realm, "trusted-issuer", assertion, DateTimeOffset.UtcNow.AddSeconds(30)));
            await table.CreateIfNotExistsAsync();
            var result = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => (i % 2 == 0 ? first : second)
                .TryClaimAsync(realm, "trusted-issuer", assertion, DateTimeOffset.UtcNow.AddSeconds(30))));
            Assert.AreEqual(1, result.Count(x => x));
            Assert.IsFalse(await second.TryClaimAsync(realm, "trusted-issuer", assertion, DateTimeOffset.UtcNow.AddSeconds(30)));
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => first.PurgeExpiredAsync(realm, DateTimeOffset.UtcNow));
            await table.AddEntityAsync(new TableEntity(realm.ToString("N"), "expired-fixture") { ["ExpiresUtc"] = DateTimeOffset.UtcNow.AddMinutes(-5) });
            Assert.AreEqual(1, await first.PurgeExpiredAsync(realm, DateTimeOffset.UtcNow.AddMinutes(-2), 10));
            Assert.IsFalse(await second.TryClaimAsync(realm, "trusted-issuer", assertion, DateTimeOffset.UtcNow.AddSeconds(30)));
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => first.TryClaimAsync(realm, "trusted-issuer", Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(61)));
        }
        finally { await table.DeleteAsync(); }
    }
}
