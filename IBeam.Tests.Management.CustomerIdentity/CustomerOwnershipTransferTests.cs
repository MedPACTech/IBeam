using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tenants;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerOwnershipTransferTests
{
    private static IdentityTenantOwnershipAdministrationService Service(MembershipStorageFixture f,
        IIdentityTenantMembershipConditionalStore? store = null, IdentityTenantOwnerMode mode = IdentityTenantOwnerMode.MultipleOwners, int limit = 5000)
        => new(store ?? f.Members, f.Roles, f.Tenants,
            Options.Create(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = [f.Owner.Name] }),
            Options.Create(new IdentityTenantOwnershipTransferOptions { Enabled = true, Mode = mode, MaximumMembers = limit }), new ServiceOperationExecutor());
    private static async Task Configure(MembershipStorageFixture f)
        => await new IdentityTenantOwnershipProtection(f.Members, f.Roles,
            Options.Create(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = [f.Owner.Name] }), new ServiceOperationExecutor()).EnsureAsync(f.Tenant);
    private static async Task<IdentityTenantOwnershipTransferRequest> Request(MembershipStorageFixture f, Guid? source = null, Guid? destination = null)
    {
        var snapshot = await f.Members.ReadOwnershipSnapshotAsync(f.Tenant, 5000);
        var a = snapshot.Members.Single(x => x.UserId == (source ?? f.AdminA));
        var b = snapshot.Members.Single(x => x.UserId == (destination ?? f.Member));
        return new(f.Tenant, a.UserId, b.UserId, a.Version, b.Version, snapshot.Version, Guid.NewGuid());
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ReadOnlyLookupUsesMembershipAuthorityAndDoesNotCreateMetadata()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true, ownerRoleName: "EnterpriseCustodian");
        var service = Service(f);
        var before = await f.Primary.GetEntityIfExistsAsync<TableEntity>("TEN|" + f.Tenant, "!IBEAM|MEMBERSHIP");
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.ReadOwnersAsync(f.Tenant));
        var after = await f.Primary.GetEntityIfExistsAsync<TableEntity>("TEN|" + f.Tenant, "!IBEAM|MEMBERSHIP");
        Assert.AreEqual(before.HasValue, after.HasValue); if (before.HasValue) Assert.AreEqual(before.Value.ETag, after.Value.ETag);
        await Configure(f);
        var tenant = (await f.TenantTable.GetEntityAsync<TableEntity>("TEN", f.Tenant.ToString("D"))).Value;
        tenant["OwnerUserId"] = f.Member.ToString("D"); await f.TenantTable.UpdateEntityAsync(tenant, tenant.ETag, TableUpdateMode.Merge);
        var guard = (await f.Primary.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "!IBEAM|MEMBERSHIP")).Value;
        var owners = await service.ReadOwnersAsync(f.Tenant);
        CollectionAssert.AreEquivalent(new[] { f.AdminA, f.AdminB }, owners.Owners.Select(x => x.UserId).ToArray());
        Assert.IsFalse(owners.Owners.Any(x => x.UserId == f.Member));
        Assert.AreEqual(guard.ETag.ToString(), owners.Version);
        Assert.AreEqual(guard.ETag, (await f.Primary.GetEntityAsync<TableEntity>(guard.PartitionKey, guard.RowKey)).Value.ETag);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task TransferPreservesOtherRolesConsumerColumnsDefaultTenantAndOtherMemberships()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Configure(f);
        await f.Roles.EnsureTenantMembershipAndGrantRolesAsync(new(f.Tenant, f.AdminA, RoleIds: [f.MemberRole.RoleId]));
        var other = (await f.Members.FindStateAsync(f.OtherTenant, f.AdminA))!;
        var tenantBefore = (await f.Tenants.FindByIdAsync(f.Tenant))!;
        var request = await Request(f); var result = await Service(f).TransferAsync(request);
        Assert.AreEqual(request.OperationId, result.OperationId);
        var source = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!; var destination = (await f.Members.FindStateAsync(f.Tenant, f.Member))!;
        CollectionAssert.AreEquivalent(new[] { f.MemberRole.RoleId }, source.RoleIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { f.MemberRole.RoleId, f.Owner.RoleId }, destination.RoleIds.ToArray());
        Assert.IsTrue(source.IsEnabled); Assert.IsTrue(destination.IsEnabled);
        Assert.AreEqual(other.Version, (await f.Members.FindStateAsync(f.OtherTenant, f.AdminA))!.Version);
        Assert.AreEqual(tenantBefore, await f.Tenants.FindByIdAsync(f.Tenant));
        foreach (var user in new[] { f.AdminA, f.Member }) {
            var row = (await f.Primary.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "USR|" + user)).Value;
            Assert.AreEqual("KEEP-CONSUMER-COLUMN", row.GetString("ConsumerColumn"));
            Assert.AreEqual("PRIVATE-CUSTOMER-EMAIL", row.GetString("Email"));
            var secondary = (await f.Secondary.GetEntityAsync<TableEntity>("USR|" + user, "TEN|" + f.Tenant)).Value;
            Assert.AreEqual(user == f.AdminA, secondary.GetBoolean("IsDefault"));
            Assert.AreEqual(row.GetString("RoleIdsCsv"), secondary.GetString("RoleIdsCsv"));
        }
        var owners = await Service(f).ReadOwnersAsync(f.Tenant);
        CollectionAssert.AreEquivalent(new[] { f.AdminB, f.Member }, owners.Owners.Select(x => x.UserId).ToArray());
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("source")] [DataRow("destination")] [DataRow("snapshot")]
    public async Task ExactVersionsRejectStaleTransfersWithoutPartialMutation(string version)
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Configure(f);
        var request = await Request(f); var beforeA = await f.Members.FindStateAsync(f.Tenant, f.AdminA); var beforeB = await f.Members.FindStateAsync(f.Tenant, f.Member);
        request = version switch { "source" => request with { SourceVersion = "stale" }, "destination" => request with { DestinationVersion = "*" }, _ => request with { OwnershipVersion = "stale" } };
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => Service(f).TransferAsync(request));
        Assert.AreEqual(beforeA!.Version, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version);
        Assert.AreEqual(beforeB!.Version, (await f.Members.FindStateAsync(f.Tenant, f.Member))!.Version);
        Assert.IsNull(await f.Members.FindOwnershipTransferAsync(f.Tenant, request.OperationId));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("disabled")] [DataRow("already-owner")] [DataRow("same-user")] [DataRow("not-owner")] [DataRow("foreign-user")]
    public async Task IneligibleTransfersFailClosedWithoutReactivation(string reason)
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Configure(f);
        if (reason == "disabled") {
            var member = (await f.Members.FindStateAsync(f.Tenant, f.Member))!;
            await new IdentityTenantMembershipAdministrationService(f.Members, f.Roles, f.Tenants, new ServiceOperationExecutor(),
                new IdentityTenantOwnershipProtection(f.Members, f.Roles, Options.Create(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = [f.Owner.Name] }), new ServiceOperationExecutor()))
                .SetEnabledAsync(f.Tenant, f.Member, false, member.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        }
        var request = await Request(f);
        request = reason switch {
            "already-owner" => request with { DestinationUserId = f.AdminB, DestinationVersion = (await f.Members.FindStateAsync(f.Tenant, f.AdminB))!.Version },
            "same-user" => request with { DestinationUserId = request.SourceUserId },
            "not-owner" => request with { SourceUserId = f.Member, DestinationUserId = f.AdminA, SourceVersion = request.DestinationVersion, DestinationVersion = request.SourceVersion },
            "foreign-user" => request with { DestinationUserId = Guid.NewGuid() }, _ => request };
        if (reason == "foreign-user") await Assert.ThrowsAsync<IdentityNotFoundException>(() => Service(f).TransferAsync(request));
        else await Assert.ThrowsAsync<IdentityValidationException>(() => Service(f).TransferAsync(request));
        Assert.IsNull(await f.Members.FindOwnershipTransferAsync(f.Tenant, request.OperationId));
        if (reason == "disabled") Assert.IsFalse((await f.Members.FindStateAsync(f.Tenant, f.Member))!.IsEnabled);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task SingleOwnerPolicyRequiresReconciliationThenTransfersWithoutAnOwnerlessState()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Configure(f);
        var service = Service(f, mode: IdentityTenantOwnerMode.SingleOwner);
        var rejected = await Request(f);
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.TransferAsync(rejected));
        var protection = new IdentityTenantOwnershipProtection(f.Members, f.Roles,
            Options.Create(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = [f.Owner.Name] }), new ServiceOperationExecutor());
        var admin = new IdentityTenantMembershipAdministrationService(f.Members, f.Roles, f.Tenants, new ServiceOperationExecutor(), protection);
        var old = (await f.Members.FindStateAsync(f.Tenant, f.AdminB))!;
        await admin.SetRolesAsync(f.Tenant, f.AdminB, [f.MemberRole], old.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        await service.TransferAsync(await Request(f));
        var owners = await service.ReadOwnersAsync(f.Tenant); Assert.HasCount(1, owners.Owners); Assert.AreEqual(f.Member, owners.Owners[0].UserId);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("lost-reply")] [DataRow("projection")]
    public async Task StableReceiptRecoversLostReplyOrProjectionFailureAndRejectsChangedIntent(string failure)
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Configure(f);
        var wrapper = new TransferStore(f.Members) { LoseReplyOnce = failure == "lost-reply", FailProjectionOnce = failure == "projection" };
        var service = Service(f, wrapper); var request = await Request(f);
        var error = await Assert.ThrowsAsync<IdentityProviderException>(() => service.TransferAsync(request)); Assert.DoesNotContain("PRIVATE", error.Message);
        var sourceVersion = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version;
        var destinationVersion = (await f.Members.FindStateAsync(f.Tenant, f.Member))!.Version;
        await service.TransferAsync(request);
        Assert.AreEqual(1, wrapper.Commits);
        Assert.AreEqual(sourceVersion, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version);
        Assert.AreEqual(destinationVersion, (await f.Members.FindStateAsync(f.Tenant, f.Member))!.Version);
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => service.TransferAsync(request with { DestinationVersion = "other" }));
        Assert.HasCount(2, (await service.ReadOwnersAsync(f.Tenant)).Owners);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RetryingAnOldReceiptCannotReapplyRolesAfterASubsequentTransfer()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Configure(f);
        var service = Service(f); var first = await Request(f); await service.TransferAsync(first);
        var second = await Request(f, f.Member, f.AdminA); await service.TransferAsync(second);
        var snapshot = await service.ReadOwnersAsync(f.Tenant); await service.TransferAsync(first);
        var after = await service.ReadOwnersAsync(f.Tenant);
        Assert.AreEqual(snapshot.Version, after.Version);
        CollectionAssert.AreEqual(snapshot.Owners.ToArray(), after.Owners.ToArray());
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task CompetingTransfersUseOneFenceAndUnappliedBatchRollsBackBothMemberships()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Configure(f);
        var barrier = new TransferStore(f.Members) { WaitForPair = true }; var service = Service(f, barrier);
        var first = await Request(f); var second = await Request(f, f.AdminB);
        async Task<bool> Attempt(IdentityTenantOwnershipTransferRequest request) {
            try { await service.TransferAsync(request); return true; } catch (IdentityConcurrencyException) { return false; }
        }
        var results = await Task.WhenAll(Attempt(first), Attempt(second)); Assert.AreEqual(1, results.Count(x => x));
        Assert.HasCount(2, (await service.ReadOwnersAsync(f.Tenant)).Owners);
        var failed = results[0] ? second : first;
        Assert.IsNull(await f.Members.FindOwnershipTransferAsync(f.Tenant, failed.OperationId));
        Assert.Contains(f.Owner.RoleId, (await f.Members.FindStateAsync(f.Tenant, failed.SourceUserId))!.RoleIds);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ProviderBatchPreconditionFailureRollsBackGuardAndSource()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Configure(f);
        var request = await Request(f); var source = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!; var destination = (await f.Members.FindStateAsync(f.Tenant, f.Member))!;
        // Fault injection: destination changes after the service snapshot without refreshing the batch's ETag.
        await f.Primary.UpdateEntityAsync(new TableEntity("TEN|" + f.Tenant, "USR|" + f.Member) { ["ConsumerColumn"] = "CONCURRENT-UPDATE" }, new ETag(destination.Version), TableUpdateMode.Merge);
        var concurrentVersion = (await f.Members.FindStateAsync(f.Tenant, f.Member))!.Version;
        var nextA = source with { RoleIds = [], RoleNames = [] };
        var nextB = destination with { RoleIds = [f.MemberRole.RoleId, f.Owner.RoleId], RoleNames = [f.MemberRole.Name, f.Owner.Name] };
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => f.Members.CommitOwnershipTransferAsync(new(nextA, nextB, request.OwnershipVersion, request.OperationId, new string('A',64))));
        Assert.AreEqual(source.Version, (await f.Members.FindStateAsync(f.Tenant, source.UserId))!.Version);
        Assert.AreEqual(concurrentVersion, (await f.Members.FindStateAsync(f.Tenant, destination.UserId))!.Version);
        Assert.AreEqual(request.OwnershipVersion, (await f.Members.ReadOwnershipSnapshotAsync(f.Tenant, 5000)).Version);
        Assert.IsNull(await f.Members.FindOwnershipTransferAsync(f.Tenant, request.OperationId));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task LookupIsBoundedAndLegacyNameOnlyMutationRequiresNormalization()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Configure(f);
        await Assert.ThrowsAsync<IdentityValidationException>(() => Service(f, limit: 2).ReadOwnersAsync(f.Tenant));
        var row = (await f.Primary.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "USR|" + f.AdminA)).Value;
        row["RoleIdsCsv"] = ""; await f.Primary.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Merge);
        Assert.IsTrue((await Service(f).ReadOwnersAsync(f.Tenant)).Owners.Any(x => x.UserId == f.AdminA));
        var request = await Request(f);
        await Assert.ThrowsAsync<IdentityValidationException>(() => Service(f).TransferAsync(request));
    }
    [TestMethod]
    public void UnsupportedProviderOrUnspecifiedPolicyIsNotSilentlyEnabled()
    {
        var ownership = Options.Create(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = ["Owner"] });
        var options = Options.Create(new IdentityTenantOwnershipTransferOptions { Enabled = true, Mode = IdentityTenantOwnerMode.MultipleOwners });
        Assert.Throws<InvalidOperationException>(() => new IdentityTenantOwnershipAdministrationService(Mock.Of<IIdentityTenantMembershipConditionalStore>(),
            Mock.Of<ITenantRoleStore>(), Mock.Of<IIdentityTenantStore>(), ownership, options, new ServiceOperationExecutor()));
        Assert.IsFalse(new IdentityTenantOwnershipTransferOptions { Enabled = true }.IsValid());
        Assert.IsFalse(new IdentityTenantOwnershipTransferOptions { Enabled = true, Mode = IdentityTenantOwnerMode.SingleOwner, MaximumMembers = 5001 }.IsValid());
        var services = new ServiceCollection(); var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        services.AddIBeamIdentityTenantOwnershipAdministration(config).AddIBeamIdentityTenantOwnershipAdministration(config);
        Assert.IsFalse(services.Any(x => x.ServiceType == typeof(IIdentityTenantOwnershipAdministrationService)));
    }

    private sealed class TransferStore(IIdentityTenantOwnershipTransferStore inner) : IIdentityTenantOwnershipTransferStore
    {
        public bool LoseReplyOnce { get; set; } public bool FailProjectionOnce { get; set; } public bool WaitForPair { get; set; }
        public int Commits { get; private set; }
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously); private int reads;
        public int OwnershipProtocolVersion => inner.OwnershipProtocolVersion; public bool RequiresOwnershipProtection => inner.RequiresOwnershipProtection;
        public async Task<IdentityTenantMembershipSnapshot> ReadOwnershipSnapshotAsync(Guid tenant, int limit, CancellationToken ct = default) {
            var result = await inner.ReadOwnershipSnapshotAsync(tenant, limit, ct);
            if (WaitForPair && Interlocked.Increment(ref reads) <= 2) { if (reads == 2) ready.TrySetResult(); await ready.Task.WaitAsync(ct); }
            return result;
        }
        public Task<IdentityTenantOwnershipTransferReceipt?> FindOwnershipTransferAsync(Guid tenant, Guid operation, CancellationToken ct = default) => inner.FindOwnershipTransferAsync(tenant, operation, ct);
        public async Task<IdentityTenantOwnershipTransferReceipt> CommitOwnershipTransferAsync(IdentityTenantOwnershipChange change, CancellationToken ct = default) {
            Commits++; var receipt = await inner.CommitOwnershipTransferAsync(change, ct);
            if (LoseReplyOnce) { LoseReplyOnce = false; throw new IdentityProviderException("PRIVATE-LOST-REPLY"); } return receipt;
        }
        public Task RepairProjectionAsync(Guid tenant, Guid user, CancellationToken ct = default) {
            if (FailProjectionOnce) { FailProjectionOnce = false; throw new IdentityProviderException("PRIVATE-PROJECTION"); } return inner.RepairProjectionAsync(tenant, user, ct);
        }
        public Task ConfigureOwnershipProtectionAsync(Guid tenant, IReadOnlyList<Guid> ids, CancellationToken ct = default) => inner.ConfigureOwnershipProtectionAsync(tenant, ids, ct);
        public Task ConfigureProtectionAsync(Guid tenant, IReadOnlyList<Guid> ids, CancellationToken ct = default) => inner.ConfigureProtectionAsync(tenant, ids, ct);
        public Task<IdentityTenantMembershipSnapshot> ReadSnapshotAsync(Guid tenant, int limit, CancellationToken ct = default) => inner.ReadSnapshotAsync(tenant, limit, ct);
        public Task<IdentityTenantMembershipState?> FindStateAsync(Guid tenant, Guid user, CancellationToken ct = default) => inner.FindStateAsync(tenant, user, ct);
        public Task<IdentityTenantMembershipState> ReplaceStateAsync(IdentityTenantMembershipState next, string memberVersion, string tenantVersion, Guid operation, CancellationToken ct = default) => inner.ReplaceStateAsync(next, memberVersion, tenantVersion, operation, ct);
    }
}
