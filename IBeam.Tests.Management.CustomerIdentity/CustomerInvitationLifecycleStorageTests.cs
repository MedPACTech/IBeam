using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Repositories.AzureTable;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerInvitationLifecycleStorageTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ConditionalNativeRotationRejectsOldTokensRepairsIndexAndPreservesConsumerColumns()
        => await WithStore(async (store, table, journal, journalTable) => {
            var row = await store.CreateAsync(Invite()); var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await table.UpdateEntityAsync(new TableEntity("TEN|" + row.TenantId, "INV|" + row.InviteId) { ["ConsumerColumn"] = "KEEP" }, new Azure.ETag(row.Version!), TableUpdateMode.Merge);
            row = (await store.GetAsync(row.TenantId, row.InviteId))!;
            var command = Resend(row, token); var changed = await store.TransitionAsync(command);
            Assert.IsTrue(changed.RequireVerifiedAcceptance);
            Assert.IsNull(await store.FindByTokenHashAsync(row.TokenHash)); Assert.AreEqual(changed.InviteId, (await store.FindByTokenHashAsync(command.TokenHash!))!.InviteId);
            Assert.AreEqual("KEEP", (await table.GetEntityAsync<TableEntity>("TEN|" + row.TenantId, "INV|" + row.InviteId)).Value.GetString("ConsumerColumn"));
            await Assert.ThrowsAsync<IdentityConcurrencyException>(() => store.UpdateAsync(row with { Status = TenantInviteStatuses.Sent }));
            await Assert.ThrowsAsync<IdentityConcurrencyException>(() => store.UpdateAsync(row with { Version = null, Status = TenantInviteStatuses.Sent }));
            await table.DeleteEntityAsync("TOK|" + command.TokenHash, "INV");
            var repaired = await store.TransitionAsync(command); Assert.AreEqual(changed.Version, repaired.Version); Assert.IsNotNull(await store.FindByTokenHashAsync(command.TokenHash!));
            Assert.AreEqual(1, repaired.ResendCount);
            var revoke = Revoke(repaired); var revoked = await store.TransitionAsync(revoke);
            Assert.AreEqual("revoked", revoked.Status); Assert.IsNull(revoked.RevokedByUserId);
            await Assert.ThrowsAsync<IdentityConcurrencyException>(() => store.BeginAcceptanceAsync(row.TenantId, row.InviteId, command.TokenHash!, Guid.NewGuid()));
            Assert.AreEqual(revoked.Version, (await store.TransitionAsync(revoke)).Version);
        });

    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ConcurrentAcceptanceVersusRevokeHasOneWinnerAndClaimsCannotBeStolenOrExpired()
        => await WithStore(async (store, table, journal, journalTable) => {
            for (var i = 0; i < 8; i++) {
                var row = await store.CreateAsync(Invite()); var customer = Guid.NewGuid();
                async Task<TenantInviteRecord?> Attempt(Func<Task<TenantInviteRecord>> action) { try { return await action(); } catch (IdentityConcurrencyException) { return null; } }
                var outcomes = await Task.WhenAll(Attempt(() => store.BeginAcceptanceAsync(row.TenantId, row.InviteId, row.TokenHash, customer)), Attempt(() => store.TransitionAsync(Revoke(row))));
                Assert.AreEqual(1, outcomes.Count(x => x is not null));
                var current = (await store.GetAsync(row.TenantId, row.InviteId))!;
                if (current.AcceptanceUserId == customer) {
                    await Assert.ThrowsAsync<IdentityConcurrencyException>(() => store.TransitionAsync(Revoke(current)));
                    await Assert.ThrowsAsync<IdentityConcurrencyException>(() => store.BeginAcceptanceAsync(row.TenantId, row.InviteId, row.TokenHash, Guid.NewGuid()));
                    await Assert.ThrowsAsync<IdentityConcurrencyException>(() => store.UpdateAsync(current with { Status = TenantInviteStatuses.Expired }));
                    await table.UpdateEntityAsync(new TableEntity("TEN|" + current.TenantId, "INV|" + current.InviteId) { ["ExpiresUtc"] = DateTimeOffset.UtcNow.AddSeconds(-1) }, new Azure.ETag(current.Version!), TableUpdateMode.Merge);
                    var resumed = await store.BeginAcceptanceAsync(row.TenantId, row.InviteId, row.TokenHash, customer);
                    Assert.AreEqual(current.AcceptanceId, resumed.AcceptanceId);
                    var redeemed = await store.UpdateAsync(resumed with { Status = TenantInviteStatuses.Redeemed, RedeemedByUserId = customer, RedeemedUtc = DateTimeOffset.UtcNow });
                    Assert.AreEqual("redeemed", redeemed.Status);
                    Assert.AreEqual(redeemed.Version, (await store.UpdateAsync(resumed with { Status = TenantInviteStatuses.Redeemed, RedeemedByUserId = customer })).Version);
                } else Assert.AreEqual("revoked", current.Status);
            }
            var expired = await store.CreateAsync(Invite() with { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1) });
            await Assert.ThrowsAsync<IdentityConcurrencyException>(() => store.BeginAcceptanceAsync(expired.TenantId, expired.InviteId, expired.TokenHash, Guid.NewGuid()));
        });

    [TestMethod, TestCategory("StorageIntegration")]
    public async Task PrivateTransitionJournalUsesExactVersionsAndRealmTenantOperationScope()
        => await WithStore(async (store, table, journal, journalTable) => {
            var f = new InvitationLifecycleFixture(); await f.Service.ResendAsync(f.Tenant, f.Id, "private-journal-intent", f.Row.Version!);
            var original = f.Journal.Rows.Values.Single(); Assert.IsTrue(await journal.TryCreateAsync(original)); Assert.IsFalse(await journal.TryCreateAsync(original));
            var saved = (await journal.GetAsync(original.Scope, original.TenantId, original.OperationId))!;
            Assert.AreEqual(original.EncryptedInput, saved.EncryptedInput); Assert.IsFalse(string.IsNullOrWhiteSpace(saved.Version));
            var changed = await journal.ReplaceAsync(saved with { DeliveryStatus = "unknown" }, saved.Version);
            await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => journal.ReplaceAsync(saved, saved.Version));
            Assert.IsNull(await journal.GetAsync(saved.Scope with { IdentityRealmId = Guid.NewGuid() }, saved.TenantId, saved.OperationId));
            Assert.IsNull(await journal.GetAsync(saved.Scope, Guid.NewGuid(), saved.OperationId));
            await foreach (var row in journalTable.QueryAsync<TableEntity>()) { var json = JsonSerializer.Serialize(row); Assert.DoesNotContain(f.LastToken!, json); Assert.DoesNotContain(f.Row.NormalizedDestination, json); }
        });

    private static TenantInviteRecord Invite() => new(Guid.NewGuid(), Guid.NewGuid(), "email", "private@example.test", Guid.NewGuid().ToString("N"),
        "pending", DateTimeOffset.UtcNow, Guid.Empty, DateTimeOffset.UtcNow.AddDays(7), RoleIds: [Guid.NewGuid()]);
    private static ManagedTenantInviteTransition Resend(TenantInviteRecord row, string token) => new() { TenantId = row.TenantId, InviteId = row.InviteId,
        OperationId = Guid.NewGuid(), Kind = "resend", ExpectedVersion = row.Version!, NowUtc = DateTimeOffset.UtcNow,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7), TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
        ResendWindowUtc = DateTimeOffset.UtcNow, ResendCount = 1, ExternalActor = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) };
    private static ManagedTenantInviteTransition Revoke(TenantInviteRecord row) => new() { TenantId = row.TenantId, InviteId = row.InviteId,
        OperationId = Guid.NewGuid(), Kind = "revoke", ExpectedVersion = row.Version!, NowUtc = DateTimeOffset.UtcNow,
        ExternalActor = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) };
    private static async Task WithStore(Func<AzureTableTenantInviteStore, TableClient, AzureTableManagementCustomerInvitationTransitionRepository, TableClient, Task> action)
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE"); if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var table = new TableClient(connection, "InviteLifecycle" + Guid.NewGuid().ToString("N")); var journal = new TableClient(connection, "InviteTransitions" + Guid.NewGuid().ToString("N"));
        try { await table.CreateIfNotExistsAsync(); await journal.CreateIfNotExistsAsync();
            var store = new AzureTableTenantInviteStore(new TableServiceClient(connection), Options.Create(new AzureTableIdentityOptions { TenantInvitesTableName = table.Name, CreateTablesIfNotExists = false }));
            await action(store, table, new(journal), journal);
        } finally { await table.DeleteAsync(); await journal.DeleteAsync(); }
    }
}
