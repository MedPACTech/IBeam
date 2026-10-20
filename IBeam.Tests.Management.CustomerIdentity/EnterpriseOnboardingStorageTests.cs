using System.Security.Cryptography;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Repositories.AzureTable;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class EnterpriseOnboardingStorageTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task StableNativeInviteRepairsMissingTokenIndexAndJournalUsesExactVersionsWithoutPlaintextRecoveryInput()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var suffix = Guid.NewGuid().ToString("N"); var table = new TableClient(connection, "OnboardInvites" + suffix); var jobs = new TableClient(connection, "OnboardJobs" + suffix);
        try {
            await table.CreateIfNotExistsAsync(); await jobs.CreateIfNotExistsAsync();
            var store = new AzureTableTenantInviteStore(new TableServiceClient(connection), Options.Create(new AzureTableIdentityOptions { TenantInvitesTableName = table.Name, CreateTablesIfNotExists = false }));
            var scope = new ManagementIdentityOperationScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var invite = new TenantInviteRecord(Guid.NewGuid(), Guid.NewGuid(), "email", "enterprise@example.test", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                "pending", DateTimeOffset.UtcNow, Guid.Empty, DateTimeOffset.UtcNow.AddDays(6), RoleIds: [Guid.NewGuid()], RequirePasswordSetup: true,
                CausationId: Guid.NewGuid().ToString("D"), Metadata: new Dictionary<string, string> { ["origin"] = "external-management" });
            var stable = (IExternalTenantInviteStore)store;
            await stable.EnsureAsync(invite);
            await table.DeleteEntityAsync("TOK|" + invite.TokenHash, "INV");
            await stable.EnsureAsync(invite);
            Assert.AreEqual(invite.InviteId, (await store.FindByTokenHashAsync(invite.TokenHash))!.InviteId);
            var row = (await table.GetEntityAsync<TableEntity>("TEN|" + invite.TenantId.ToString("D"), "INV|" + invite.InviteId.ToString("D"))).Value;
            row["Status"] = "redeemed";
            await table.UpdateEntityAsync(row, row.ETag);
            Assert.AreEqual("redeemed", (await store.FindByTokenHashAsync(invite.TokenHash))!.Status); // stale pending token index cannot resurrect it.
            await Assert.ThrowsAsync<IdentityValidationException>(() => stable.EnsureAsync(invite with { NormalizedDestination = "different@example.test" }));
            var repo = new AzureTableManagementEnterpriseOnboardingRepository(jobs);
            var job = new ManagementEnterpriseOnboardingEntity { Scope = scope, ApplicationKey = "atlas", Environment = "Testing", OperationId = Guid.NewGuid(),
                OnboardingId = Guid.NewGuid(), InviteId = invite.InviteId, CreatedByUserId = Guid.NewGuid(), CreatedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(6), EncryptedInput = "authenticated-ciphertext-fixture" };
            Assert.IsTrue(await repo.TryCreateAsync(job)); Assert.IsFalse(await repo.TryCreateAsync(job));
            var read = (await repo.GetAsync(scope, job.OnboardingId))!; Assert.AreEqual(job.EncryptedInput, read.EncryptedInput);
            var changed = await repo.ReplaceAsync(read with { Step = "extensions-completed" }, read.Version);
            Assert.AreNotEqual(read.Version, changed.Version);
            await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => repo.ReplaceAsync(read with { Step = "tenant-extensions" }, read.Version));
            await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => repo.ReplaceAsync(read, "*"));
            Assert.IsNull(await repo.GetAsync(scope with { OrganizationId = Guid.NewGuid() }, job.OnboardingId));
            await foreach (var entity in jobs.QueryAsync<TableEntity>()) {
                Assert.DoesNotContain("enterprise@example.test", JsonSerializer.Serialize(entity));
                Assert.DoesNotContain("EncryptedInput", entity.GetString("Metadata"));
            }
        } finally { await table.DeleteAsync(); await jobs.DeleteAsync(); }
    }
    [TestMethod]
    public async Task SignedCompositePrerequisitesRetainOriginalActorAndRecheckTheCompleteLiveCredential()
    {
        await using var f = await DelegationFixture.Start(compositeOnboarding: true);
        const string path = "/api/management-identity/probe/onboarding";
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.OnboardingCreate, "POST", path), "POST", path);
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, text);
        using var doc = JsonDocument.Parse(text); var data = doc.RootElement.GetProperty("data");
        Assert.AreEqual(ManagementCustomerIdentityOperations.OnboardingCreate, data.GetProperty("original").GetString());
        Assert.AreEqual(ManagementCustomerIdentityOperations.TenantsCreate, data.GetProperty("child").GetString());
        Assert.IsTrue(data.GetProperty("denied").GetBoolean()); Assert.AreEqual(f.User, data.GetProperty("userId").GetGuid());
    }
}
