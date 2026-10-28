using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Services.Tenants;
using IBeam.Management.SyntheticConsumer;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
[TestCategory("StorageIntegration")]
public sealed class MembershipProjectionTests
{
    [TestMethod]
    public async Task OriginalEventIsDurablyDeduplicatedAcrossRestartWithoutNativeOrBusinessWrites()
    {
        await using var f = await Fixture.Create(); var evt = await f.Change(false); var native = await f.NativeVersion();
        await f.Service.DeliverAsync(evt); var first = await f.Row(); var version = await f.Store.GetVersionAsync(f.Scope);
        await f.Restart().DeliverAsync(evt);
        Assert.AreEqual(version, await f.Store.GetVersionAsync(f.Scope)); Assert.AreEqual(first.ETag, (await f.Row()).ETag);
        Assert.AreEqual(native, await f.NativeVersion()); Assert.IsFalse(first.GetBoolean("NativeEnabled"));
        Assert.AreEqual("PRIVATE-SYNTHETIC-APP-DATA", first.GetString("BusinessCanary"));
        var receipt = await f.Table.GetEntityAsync<TableEntity>(SyntheticMembershipProjectionStore.Partition(f.Scope), "R|event|" + evt.EventId.ToString("D"));
        Assert.DoesNotContain("PRIVATE-", receipt.Value.GetString("Receipt")!);
    }
    [TestMethod]
    public async Task DelayedOlderEventReadsCurrentNativeStateAndNeverRollsBackNewerProjection()
    {
        await using var f = await Fixture.Create(); var old = await f.Change(false); var newer = await f.Change(true);
        await f.Service.DeliverAsync(old); Assert.IsTrue((await f.Row()).GetBoolean("NativeEnabled"));
        Assert.AreEqual(newer.Changes[0].After!.Revision, (await f.Row()).GetGuid("NativeRevision"));
        await f.Service.DeliverAsync(newer); await f.Service.DeliverAsync(old);
        Assert.IsTrue((await f.Row()).GetBoolean("NativeEnabled")); Assert.AreEqual(newer.Sequence, (await f.Row()).GetInt64("NativeSequence"));
    }
    [TestMethod]
    public async Task BoundedRepairReadsOnlySelectedAuthoritativeMetadataAndRetainsBusinessColumns()
    {
        await using var f = await Fixture.Create(); var intent = f.Repair(); var native = await f.NativeVersion();
        var receipt = await f.Service.RepairAsync(intent, [f.Native.OtherAdmin], await f.Service.GetVersionAsync(f.Scope));
        Assert.AreEqual(1, receipt.Applied); Assert.AreEqual(native, await f.NativeVersion());
        var row = await f.Row(); row["BusinessCanary"] = "PRIVATE-RETAINED"; await f.Table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace);
        await f.Change(false); var repair = f.Repair();
        await f.Service.RepairAsync(repair, [f.Native.OtherAdmin], await f.Service.GetVersionAsync(f.Scope));
        Assert.AreEqual("PRIVATE-RETAINED", (await f.Row()).GetString("BusinessCanary"));
        Assert.IsFalse((await f.Row()).GetBoolean("NativeEnabled"));
        Assert.IsFalse((await f.Table.GetEntityIfExistsAsync<TableEntity>(SyntheticMembershipProjectionStore.Partition(f.Scope), "M|" + f.Native.User.ToString("D"))).HasValue);
    }
    [TestMethod]
    public async Task MissingNativeMembershipBecomesMetadataTombstoneWithoutGrantingAnything()
    {
        await using var f = await Fixture.Create(); var unknown = Guid.NewGuid();
        await f.Service.RepairAsync(f.Repair(), [unknown], await f.Service.GetVersionAsync(f.Scope));
        var row = await f.Row(unknown); Assert.IsTrue(row.GetBoolean("NativeMissing")); Assert.IsFalse(row.GetBoolean("NativeEnabled"));
        Assert.AreEqual("", row.GetString("NativeRoleIds")); Assert.IsNull(await f.Members.FindStateAsync(f.Native.Tenant, unknown));
    }
    [TestMethod]
    public async Task SameIntentLostReplyRecoversOriginalReceiptAfterRestartAndRejectsChangedSelectionOrActor()
    {
        await using var f = await Fixture.Create(); var intent = f.Repair(); var version = await f.Service.GetVersionAsync(f.Scope);
        f.Store.AfterCommitForTesting = () => throw new IOException("PRIVATE-LOST-REPLY");
        var error = await Assert.ThrowsAsync<IdentityProviderException>(() => f.Service.RepairAsync(intent, [f.Native.OtherAdmin], version));
        Assert.DoesNotContain("PRIVATE-", error.ToString()); var restarted = f.Restart();
        var original = await restarted.FindRepairAsync(intent); Assert.IsNotNull(original);
        Assert.AreEqual(original, await restarted.RepairAsync(intent, [f.Native.OtherAdmin], version));
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => restarted.RepairAsync(intent, [f.Native.User], version));
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => restarted.FindRepairAsync(intent with { Actor = intent.Actor with { UserId = Guid.NewGuid() } }));
    }
    [TestMethod]
    public async Task StaleProjectionVersionCannotChangeRowsOrCreateReceipt()
    {
        await using var f = await Fixture.Create(); var stale = await f.Service.GetVersionAsync(f.Scope);
        await f.Service.RepairAsync(f.Repair(), [f.Native.User], stale); var intent = f.Repair();
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => f.Service.RepairAsync(intent, [f.Native.OtherAdmin], stale));
        Assert.IsNull(await f.Service.FindRepairAsync(intent));
    }
    [TestMethod]
    [DataRow(0)] [DataRow(97)]
    public async Task RepairRejectsUnboundedOrEmptySelection(int count)
    {
        await using var f = await Fixture.Create(); var version = await f.Service.GetVersionAsync(f.Scope);
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Service.RepairAsync(f.Repair(), Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray(), version));
    }
    [TestMethod]
    public async Task DuplicateOrEmptyIdsAndWildcardVersionAreRejected()
    {
        await using var f = await Fixture.Create(); var v = await f.Service.GetVersionAsync(f.Scope);
        foreach (var ids in new Guid[][] { [Guid.Empty], [f.Native.User, f.Native.User] })
            await Assert.ThrowsAsync<IdentityValidationException>(() => f.Service.RepairAsync(f.Repair(), ids, v));
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Service.RepairAsync(f.Repair(), [f.Native.User], "*"));
    }
    [TestMethod]
    [DataRow("application")] [DataRow("environment")] [DataRow("realm")] [DataRow("tenant")]
    public async Task WrongScopeCannotReadRepairOrDeduplicate(string field)
    {
        await using var f = await Fixture.Create(); var wrong = field switch { "application" => f.Scope with { ApplicationKey = "other" },
            "environment" => f.Scope with { Environment = "Production" }, "realm" => f.Scope with { RealmId = Guid.NewGuid() }, _ => f.Scope with { TenantId = Guid.NewGuid() } };
        await Assert.ThrowsAsync<IdentityProviderException>(() => f.Service.GetVersionAsync(wrong));
        await Assert.ThrowsAsync<IdentityProviderException>(() => f.Service.RepairAsync(f.Repair() with { Scope = wrong }, [f.Native.User], "version"));
    }
    [TestMethod]
    public async Task AppPurgeBlocksLateEventAndRepairIncludingAfterSyntheticReopening()
    {
        await using var f = await Fixture.Create(); var evt = await f.Change(false); await f.Service.DeliverAsync(evt);
        await f.Store.BlockForPurgeAsync(f.Scope); await f.Store.DeleteSyntheticProjectionRowsAsync(f.Scope);
        var restarted = f.Restart(); await f.Store.SeedAsync(f.Scope); // Existing blocked control must survive seeding.
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => restarted.DeliverAsync(evt));
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => restarted.RepairAsync(f.Repair(), [f.Native.User], "version"));
        Assert.IsFalse((await f.Table.GetEntityIfExistsAsync<TableEntity>(SyntheticMembershipProjectionStore.Partition(f.Scope), "M|" + f.Native.OtherAdmin.ToString("D"))).HasValue);
    }
    [TestMethod]
    public async Task NativePurgeFenceRejectsLateProjectionWithoutRecreatingNativeOrAppRows()
    {
        await using var f = await Fixture.Create(); var evt = await f.Change(false); await f.Native.Confirm(); await f.Native.Complete();
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => f.Service.DeliverAsync(evt));
        Assert.IsFalse((await f.Table.GetEntityIfExistsAsync<TableEntity>(SyntheticMembershipProjectionStore.Partition(f.Scope), "M|" + f.Native.OtherAdmin.ToString("D"))).HasValue);
        Assert.IsNull(await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin));
    }
    [TestMethod]
    public async Task SnapshotChangeBetweenReadAndCommitFailsWithoutProjectionWrites()
    {
        await using var f = await Fixture.Create(); var source = new Mock<IIdentityMembershipProjectionSource>(); source.SetupGet(x => x.ProtocolVersion).Returns(1);
        source.Setup(x => x.RequireScopeAsync(f.Scope, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        source.Setup(x => x.ReadCurrentAsync(f.Scope, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>())).Returns(async () => {
            var snapshot = await f.Source.ReadCurrentAsync(f.Scope, [f.Native.OtherAdmin]); await f.Change(false); return snapshot;
        });
        source.Setup(x => x.RequireCurrentAsync(It.IsAny<IdentityMembershipProjectionSnapshot>(), It.IsAny<CancellationToken>())).Returns<IdentityMembershipProjectionSnapshot, CancellationToken>(f.Source.RequireCurrentAsync);
        var service = new IdentityMembershipProjectionService(source.Object, f.Store, f.Store, new ServiceOperationExecutor()); var intent = f.Repair();
        var version = await f.Store.GetVersionAsync(f.Scope);
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => service.RepairAsync(intent, [f.Native.OtherAdmin], version));
        Assert.IsNull(await f.Service.FindRepairAsync(intent));
    }
    [TestMethod]
    public async Task AppLocalGuardPreventsCommitWhenPurgeStartsAfterNativeSnapshotCheck()
    {
        await using var f = await Fixture.Create(); var current = await f.Source.ReadCurrentAsync(f.Scope, [f.Native.OtherAdmin]); var version = await f.Store.GetVersionAsync(f.Scope);
        var intent = f.Repair() with { IntentHash = new string('A', 64) }; var fence = new Mock<IIdentityMembershipProjectionCommitFence>();
        fence.Setup(x => x.RequireAsync(It.IsAny<CancellationToken>())).Returns(() => f.Store.BlockForPurgeAsync(f.Scope));
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => f.Store.ApplyAsync(intent, current, version, fence.Object));
        Assert.IsFalse((await f.Table.GetEntityIfExistsAsync<TableEntity>(SyntheticMembershipProjectionStore.Partition(f.Scope), "M|" + f.Native.OtherAdmin.ToString("D"))).HasValue);
    }
    [TestMethod]
    public async Task AppStoreRefusesOlderSubjectRevisionEvenWithAFreshProjectionVersion()
    {
        await using var f = await Fixture.Create(); var old = await f.Source.ReadCurrentAsync(f.Scope, [f.Native.OtherAdmin]);
        var newer = await f.Change(false); await f.Service.DeliverAsync(newer);
        var fence = new Mock<IIdentityMembershipProjectionCommitFence>(); fence.Setup(x => x.RequireAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var receipt = await f.Store.ApplyAsync(f.Repair() with { IntentHash = new string('B', 64) }, old, await f.Store.GetVersionAsync(f.Scope), fence.Object);
        Assert.AreEqual(0, receipt.Applied); Assert.AreEqual(1, receipt.Skipped); Assert.IsFalse((await f.Row()).GetBoolean("NativeEnabled"));
        Assert.AreEqual(newer.Sequence, (await f.Row()).GetInt64("NativeSequence"));
    }
    [TestMethod]
    public async Task TwoMemberProjectionIsAtomicWhenOneRowConflictsAtCommit()
    {
        await using var f = await Fixture.Create(); await f.Service.RepairAsync(f.Repair(), [f.Native.User, f.Native.OtherAdmin], await f.Service.GetVersionAsync(f.Scope));
        await f.Change(false); var current = await f.Source.ReadCurrentAsync(f.Scope, [f.Native.User, f.Native.OtherAdmin]);
        var version = await f.Store.GetVersionAsync(f.Scope); var owner = await f.Row(f.Native.User); var member = await f.Row();
        // Force both rows to need metadata repair, then introduce a competing writer after their ETags were read.
        owner["NativeEnabled"] = false; await f.Table.UpdateEntityAsync(owner, owner.ETag, TableUpdateMode.Replace);
        var fence = new Mock<IIdentityMembershipProjectionCommitFence>(); fence.Setup(x => x.RequireAsync(It.IsAny<CancellationToken>())).Returns(async () => {
            var row = await f.Row(); row["BusinessCanary"] = "PRIVATE-CONCURRENT"; await f.Table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace);
        });
        var intent = f.Repair() with { IntentHash = new string('C', 64) };
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => f.Store.ApplyAsync(intent, current, version, fence.Object));
        Assert.IsFalse((await f.Row(f.Native.User)).GetBoolean("NativeEnabled")); Assert.IsTrue((await f.Row()).GetBoolean("NativeEnabled"));
        Assert.AreEqual(version, await f.Store.GetVersionAsync(f.Scope)); Assert.IsNull(await f.Service.FindRepairAsync(intent));
        Assert.AreEqual("PRIVATE-CONCURRENT", (await f.Row()).GetString("BusinessCanary"));
    }
    [TestMethod]
    public async Task AtomicOwnerTransferProjectsBothCurrentMembershipsWithoutChangingNativeOwnershipAgain()
    {
        await using var f = await Fixture.Create(); using var services = f.Native.Provider.CreateScope();
        var ownership = new IdentityTenantOwnershipAdministrationService(f.Members, f.Roles, services.ServiceProvider.GetRequiredService<AzureTableIdentityTenantStore>(),
            Options.Create(new IBeam.Identity.Options.IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = ["Owner"] }),
            Options.Create(new IBeam.Identity.Options.IdentityTenantOwnershipTransferOptions { Enabled = true, Mode = IBeam.Identity.Models.IdentityTenantOwnerMode.SingleOwner }), new ServiceOperationExecutor());
        var snapshot = await f.Members.ReadOwnershipSnapshotAsync(f.Native.Tenant, 100); var operation = Guid.NewGuid();
        await ownership.TransferAsync(new(f.Native.Tenant, f.Native.User, f.Native.OtherAdmin, snapshot.Members.Single(x => x.UserId == f.Native.User).Version,
            snapshot.Members.Single(x => x.UserId == f.Native.OtherAdmin).Version, snapshot.Version, operation));
        var evt = (await f.Source.FindByOperationAsync(f.Scope, operation))!; var nativeVersion = (await f.Members.ReadOwnershipSnapshotAsync(f.Native.Tenant, 100)).Version;
        await f.Service.DeliverAsync(evt); await f.Restart().DeliverAsync(evt);
        foreach (var change in evt.Changes) Assert.AreEqual(change.After!.Revision, (await f.Row(change.UserId)).GetGuid("NativeRevision"));
        Assert.AreEqual(nativeVersion, (await f.Members.ReadOwnershipSnapshotAsync(f.Native.Tenant, 100)).Version);
    }
    [TestMethod]
    public void RegistrationIsOptionalAndDoesNotOverwriteExistingReceiversOrInstallHttpRepair()
    {
        var services = new ServiceCollection(); services.AddIBeamIdentityMembershipProjection(); Assert.HasCount(0, services);
        services.AddSingleton(Mock.Of<IIdentityMembershipEventDelivery>());
        Assert.Throws<InvalidOperationException>(() => services.AddIBeamIdentityMembershipProjection(true));
        Assert.IsFalse(typeof(IIdentityMembershipProjectionService).Assembly.GetReferencedAssemblies().Any(x => x.Name!.StartsWith("IBeam.Management", StringComparison.Ordinal)));
    }
    [TestMethod]
    public async Task InvalidProviderReceiptCountsCannotOverflowTheMetadataBound()
    {
        await using var f = await Fixture.Create(); var intent = f.Repair(); var store = new Mock<IIdentityMembershipProjectionStore>();
        store.SetupGet(x => x.ProtocolVersion).Returns(1); store.SetupGet(x => x.Durable).Returns(true);
        store.Setup(x => x.FindAsync(It.IsAny<IdentityMembershipProjectionIntent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityMembershipProjectionReceipt(intent.Id, intent.Kind, f.Scope, "version", int.MaxValue, int.MaxValue));
        var service = new IdentityMembershipProjectionService(f.Source, store.Object, f.Store, new ServiceOperationExecutor());
        await Assert.ThrowsAsync<IdentityProviderException>(() => service.FindRepairAsync(intent));
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public IdentityPurgeFixture Native = null!; public AzureTableIdentityMembershipEventOutboxStore Source = null!;
        public AzureTableTenantMembershipStore Members = null!; public AzureTableTenantRoleStore Roles = null!;
        public IdentityMembershipEventScope Scope = null!; public TableClient Table = null!;
        public SyntheticMembershipProjectionStore Store = null!; public IdentityMembershipProjectionService Service = null!;
        private IServiceScope services = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture { Native = await IdentityPurgeFixture.Create(membershipEvents: true) };
            f.services = f.Native.Provider.CreateScope(); var p = f.services.ServiceProvider;
            f.Members = p.GetRequiredService<AzureTableTenantMembershipStore>(); f.Roles = p.GetRequiredService<AzureTableTenantRoleStore>();
            f.Source = p.GetRequiredService<AzureTableIdentityMembershipEventOutboxStore>();
            f.Scope = new("fixture", "Testing", f.Native.Execution.Authority.RealmId, f.Native.Tenant);
            f.Table = f.Native.Table("SyntheticProjection"); await f.Table.CreateIfNotExistsAsync();
            f.Service = f.Restart(); await f.Store.SeedAsync(f.Scope); return f;
        }
        public IdentityMembershipProjectionService Restart()
        { Store = new(Table, "Testing", Scope.RealmId, [Native.Tenant]); return Service = new(Source, Store, Store, new ServiceOperationExecutor()); }
        public IdentityMembershipProjectionIntent Repair() => new(Guid.NewGuid(), "repair", Scope, new(IdentityMembershipActorKind.System, null, null, null, null));
        public async Task<TableEntity> Row(Guid? user = null) => (await Table.GetEntityAsync<TableEntity>(SyntheticMembershipProjectionStore.Partition(Scope), "M|" + (user ?? Native.OtherAdmin).ToString("D"))).Value;
        public async Task<string> NativeVersion() => (await Members.FindStateAsync(Native.Tenant, Native.OtherAdmin))!.Version;
        public async Task<TenantMembershipChangedEvent> Change(bool enabled)
        {
            var admin = new IdentityTenantMembershipAdministrationService(Members, Roles, services.ServiceProvider.GetRequiredService<AzureTableIdentityTenantStore>(), new ServiceOperationExecutor());
            var roles = (await Roles.GetRolesAsync(Native.Tenant)).Where(x => x.Name == "Administrator").Select(x => x.RoleId).ToArray(); var id = Guid.NewGuid();
            await admin.SetEnabledAsync(Native.Tenant, Native.OtherAdmin, enabled, await NativeVersion(), id, roles);
            return (await Source.FindByOperationAsync(Scope, id))!;
        }
        public async ValueTask DisposeAsync() { services.Dispose(); if (!Table.Uri.IsLoopback || !Table.Name.EndsWith("SyntheticProjection", StringComparison.Ordinal)) throw new InvalidOperationException(); await Table.DeleteAsync(); await Native.DisposeAsync(); }
    }
}
