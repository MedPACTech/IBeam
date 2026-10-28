using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Services.Tenants;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
[TestCategory("StorageIntegration")]
public sealed class MembershipOutboxTests
{
    [TestMethod]
    public async Task NativeCommitAndImmutableEventPersistTogetherAndDispatchNeverChangesTheMembershipAgain()
    {
        await using var f = await Fixture.Create(); var operation = Guid.NewGuid();
        await f.Change(false, operation);
        var evt = (await f.Store.FindByOperationAsync(f.Scope, operation))!;
        Assert.AreEqual("membership-disabled", evt.Kind); Assert.AreEqual(f.InitialSequence + 1, evt.Sequence);
        Assert.AreEqual(f.Native.OtherAdmin, evt.Changes.Single().UserId); Assert.IsTrue(evt.Changes[0].Before!.IsEnabled); Assert.IsFalse(evt.Changes[0].After!.IsEnabled);
        Assert.AreEqual(f.Native.User, evt.Actor.UserId); Assert.AreEqual(IdentityMembershipActorKind.Customer, evt.Actor.Kind);
        var version = (await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin))!.Version;
        await f.Dispatch.RunAsync(f.Scope); await f.Dispatch.RunAsync(f.Scope);
        Assert.AreEqual(1, f.Receiver.Calls); Assert.AreEqual(1, f.Receiver.Effects.Count);
        Assert.AreEqual(version, (await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin))!.Version);
        Assert.AreEqual(JsonSerializer.Serialize(evt), JsonSerializer.Serialize(await f.Store.FindByOperationAsync(f.Scope, operation)));
        Assert.IsNull(await f.Store.ReadFirstUndeliveredAsync(f.Scope)); Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(evt));
    }
    [TestMethod]
    [DataRow(true)] [DataRow(false)]
    public async Task LostNativeCommitReplyEitherLeavesNoChangeOrPreservesTheOriginalCommittedEvent(bool beforeCommit)
    {
        await using var f = await Fixture.Create(); var operation = Guid.NewGuid(); var before = (await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin))!;
        f.Native.Storage.FailEventBeforeCommit = beforeCommit;
        f.Native.Storage.EventFault = actions => actions.Any(x => x.ActionType == TableTransactionActionType.Add && x.Entity.RowKey.StartsWith("!IBEAM|MEMBERSHIP|EVENT|", StringComparison.Ordinal));
        var error = await Assert.ThrowsAsync<IdentityProviderException>(() => f.Change(false, operation)); Assert.DoesNotContain("PRIVATE-", error.ToString());
        var stored = await f.Store.FindByOperationAsync(f.Scope, operation); var current = (await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin))!;
        if (beforeCommit) { Assert.IsNull(stored); Assert.AreEqual(before.Version, current.Version); Assert.IsTrue(current.IsEnabled); }
        else {
            Assert.IsNotNull(stored); Assert.IsFalse(current.IsEnabled); await f.Change(false, operation);
            Assert.AreEqual(stored.EventId, (await f.Store.FindByOperationAsync(f.Scope, operation))!.EventId);
            Assert.HasCount(1, (await f.Store.ReadPageAsync(f.Scope, f.InitialSequence, 100)).Events);
        }
    }
    [TestMethod]
    public async Task NoOpReplayAndRejectedLastOwnerChangeInventNoNewLogicalEvents()
    {
        await using var f = await Fixture.Create(); var operation = Guid.NewGuid(); await f.Change(false, operation);
        var original = (await f.Store.FindByOperationAsync(f.Scope, operation))!;
        await f.Change(false, operation); var noOp = Guid.NewGuid(); await f.Change(false, noOp);
        Assert.IsNull(await f.Store.FindByOperationAsync(f.Scope, noOp));
        var owner = (await f.Members.FindStateAsync(f.Native.Tenant, f.Native.User))!;
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Admin.SetEnabledAsync(f.Native.Tenant, f.Native.User, false, owner.Version, Guid.NewGuid(), f.Administrators));
        var page = await f.Store.ReadPageAsync(f.Scope, f.InitialSequence, 100); Assert.HasCount(1, page.Events); Assert.AreEqual(original.EventId, page.Events[0].EventId);
    }
    [TestMethod]
    public async Task NativeBootstrapGrantsAndVersionedReplacementShareTheSameOutbox()
    {
        await using var f = await Fixture.Create(); var role = await f.Roles.CreateRoleAsync(f.Native.Tenant, "Member");
        using (IdentityMembershipEventActorContext.Push(new(IdentityMembershipActorKind.Customer, f.Native.User, null, null, null)))
            await f.Roles.EnsureTenantMembershipAndGrantRolesAsync(new(f.Native.Tenant, f.Native.OtherAdmin, "PRIVATE-TENANT", RoleNames: [role.Name]));
        var current = (await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin))!;
        await f.Admin.SetRolesAsync(f.Native.Tenant, f.Native.OtherAdmin, [], current.Version, Guid.NewGuid(), f.Administrators);
        var page = await f.Store.ReadPageAsync(f.Scope, f.InitialSequence, 100); Assert.HasCount(2, page.Events);
        Assert.AreEqual("roles-granted", page.Events[0].Kind); Assert.AreEqual("roles-revoked", page.Events[1].Kind);
        Assert.IsTrue(page.Events[1].Sequence > page.Events[0].Sequence); Assert.AreEqual(page.Events[0].Changes[0].After!.Revision, page.Events[1].Changes[0].Before!.Revision);
    }
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task NativeOwnerTransferPersistsOneEventForBothMembershipsAndReplaysItsOriginalReceipt(bool lostReply)
    {
        await using var f = await Fixture.Create(); using var scope = f.Native.Provider.CreateScope();
        var service = new IdentityTenantOwnershipAdministrationService(f.Members, f.Roles, scope.ServiceProvider.GetRequiredService<AzureTableIdentityTenantStore>(),
            Options.Create(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = ["Owner"] }),
            Options.Create(new IdentityTenantOwnershipTransferOptions { Enabled = true, Mode = IdentityTenantOwnerMode.SingleOwner }), new ServiceOperationExecutor());
        var snapshot = await f.Members.ReadOwnershipSnapshotAsync(f.Native.Tenant, 100);
        var request = new IdentityTenantOwnershipTransferRequest(f.Native.Tenant, f.Native.User, f.Native.OtherAdmin,
            snapshot.Members.Single(x => x.UserId == f.Native.User).Version, snapshot.Members.Single(x => x.UserId == f.Native.OtherAdmin).Version, snapshot.Version, Guid.NewGuid());
        if (lostReply) f.Native.Storage.EventFault = actions => actions.Any(x => x.ActionType == TableTransactionActionType.Add && x.Entity.RowKey.StartsWith("!IBEAM|MEMBERSHIP|EVENT|", StringComparison.Ordinal));
        using (IdentityMembershipEventActorContext.Push(new(IdentityMembershipActorKind.Customer, f.Native.User, null, null, Guid.NewGuid()))) {
            if (lostReply) await Assert.ThrowsAsync<IdentityProviderException>(() => service.TransferAsync(request)); else await service.TransferAsync(request);
        }
        using (IdentityMembershipEventActorContext.Push(new(IdentityMembershipActorKind.Support, f.Native.Actor, Guid.NewGuid(), Guid.NewGuid(), null))) await service.TransferAsync(request);
        var evt = (await f.Store.FindByOperationAsync(f.Scope, request.OperationId))!; Assert.AreEqual("ownership-transferred", evt.Kind); Assert.HasCount(2, evt.Changes);
        Assert.AreEqual(IdentityMembershipActorKind.Customer, evt.Actor.Kind); Assert.AreEqual(f.Native.User, evt.Actor.UserId);
        Assert.HasCount(1, (await f.Store.ReadPageAsync(f.Scope, f.InitialSequence, 100)).Events);
        await f.Dispatch.RunAsync(f.Scope); Assert.AreEqual(1, f.Receiver.Calls);
    }
    [TestMethod]
    public async Task LostReceiverReplyRedeliversTheSameEventWithoutRepeatingNativeMutationOrConsumerEffect()
    {
        await using var f = await Fixture.Create(); await f.Change(false, Guid.NewGuid());
        var version = (await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin))!.Version;
        f.Receiver.FailuresRemaining = 1; await f.Dispatch.RunAsync(f.Scope);
        var pending = (await f.Store.ReadFirstUndeliveredAsync(f.Scope))!; Assert.AreEqual("pending", pending.State); Assert.AreEqual("consumer-unavailable", pending.FailureCode);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(pending)); f.Clock.Now = f.Clock.Now.AddSeconds(2); await f.Dispatch.RunAsync(f.Scope);
        Assert.AreEqual(2, f.Receiver.Calls); Assert.AreEqual(1, f.Receiver.Effects.Count); Assert.IsNull(await f.Store.ReadFirstUndeliveredAsync(f.Scope));
        Assert.AreEqual(version, (await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin))!.Version);
    }
    [TestMethod]
    public async Task LostAcknowledgementReplyDoesNotChangePayloadOrRedeliverAnAlreadyAcknowledgedEvent()
    {
        await using var f = await Fixture.Create(); await f.Change(false, Guid.NewGuid());
        f.Native.Storage.EventFault = actions => actions.Any(x => x.Entity is TableEntity row && row.RowKey.StartsWith("!IBEAM|MEMBERSHIP|EVENT|", StringComparison.Ordinal) && row.GetString("State") == "acked");
        var error = await Assert.ThrowsAsync<IdentityProviderException>(() => f.Dispatch.RunAsync(f.Scope)); Assert.DoesNotContain("PRIVATE-", error.ToString());
        Assert.IsNull(await f.Store.ReadFirstUndeliveredAsync(f.Scope)); await f.Dispatch.RunAsync(f.Scope);
        Assert.AreEqual(1, f.Receiver.Calls); Assert.AreEqual(1, f.Receiver.Effects.Count);
    }
    [TestMethod]
    public async Task ConcurrentWorkersAndExpiredLeasesKeepOneOriginalEventAndRejectStaleAcknowledgements()
    {
        await using var f = await Fixture.Create(); await f.Change(false, Guid.NewGuid());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Receiver.Before = async call => { if (call == 1) { entered.TrySetResult(); await release.Task; } };
        var first = f.Dispatch.RunAsync(f.Scope, 1); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await f.Dispatch.RunAsync(f.Scope, 1); Assert.AreEqual(1, f.Receiver.Calls);
        f.Clock.Now = f.Clock.Now.AddSeconds(46); await f.Dispatch.RunAsync(f.Scope, 1); release.TrySetResult(); await first;
        Assert.AreEqual(2, f.Receiver.Calls); Assert.AreEqual(1, f.Receiver.Effects.Count); Assert.IsNull(await f.Store.ReadFirstUndeliveredAsync(f.Scope));
    }
    [TestMethod]
    public async Task RetryLimitDeadLettersBlockLaterEventsAndTrustedExactVersionRepairPreservesIds()
    {
        await using var f = await Fixture.Create(); await f.Change(false, Guid.NewGuid()); await f.Change(true, Guid.NewGuid());
        f.Receiver.FailuresRemaining = 10;
        for (var i = 0; i < 3; i++) { await f.Dispatch.RunAsync(f.Scope); f.Clock.Now = f.Clock.Now.AddSeconds(5); }
        var dead = (await f.Store.ReadFirstUndeliveredAsync(f.Scope))!; Assert.AreEqual("dead-letter", dead.State); Assert.AreEqual(3, f.Receiver.Calls);
        await f.Dispatch.RunAsync(f.Scope); Assert.AreEqual(3, f.Receiver.Calls);
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => f.Dispatch.RetryDeadLetterAsync(f.Scope, dead.Event.EventId, "stale"));
        f.Receiver.FailuresRemaining = 0; await f.Dispatch.RetryDeadLetterAsync(f.Scope, dead.Event.EventId, dead.Version); await f.Dispatch.RunAsync(f.Scope);
        Assert.AreEqual(2, f.Receiver.Effects.Count); Assert.IsNull(await f.Store.ReadFirstUndeliveredAsync(f.Scope));
    }
    [TestMethod]
    [DataRow("application")] [DataRow("environment")] [DataRow("realm")]
    public async Task WrongDeploymentCannotReadOrLeaseNativeEvents(string change)
    {
        await using var f = await Fixture.Create();
        var other = change switch { "application" => f.Scope with { ApplicationKey = "other" }, "environment" => f.Scope with { Environment = "Production" }, _ => f.Scope with { RealmId = Guid.NewGuid() } };
        await Assert.ThrowsAsync<IdentityProviderException>(() => f.Store.ReadPageAsync(other, 0, 1));
        await Assert.ThrowsAsync<IdentityProviderException>(() => f.Dispatch.RunAsync(other)); Assert.AreEqual(0, f.Receiver.Calls);
    }
    [TestMethod]
    public async Task EnrolledWritersCannotSilentlyDisableEventsAndPurgePreventsLateDeliveryOrRecreation()
    {
        await using var f = await Fixture.Create(); f.Native.NativeOptions.RequireMembershipLifecycleEvents = false;
        await Assert.ThrowsAsync<IdentityProviderException>(() => f.Change(false, Guid.NewGuid()));
        f.Native.NativeOptions.RequireMembershipLifecycleEvents = true; await f.Change(false, Guid.NewGuid());
        await f.Native.Confirm(); await f.Native.Run();
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => f.Dispatch.RunAsync(f.Scope)); Assert.AreEqual(0, f.Receiver.Calls);
        await f.Native.Complete();
        Assert.HasCount(0, (await f.Store.ReadPageAsync(f.Scope, 0, 100)).Events);
    }
    [TestMethod]
    public async Task PendingScanIsBoundedMetadataOnlyAndNeverCrossesTheConfiguredRealm()
    {
        await using var f = await Fixture.Create(); await f.Change(false, Guid.NewGuid()); var scopes = new HashSet<Guid>(); string? cursor = null;
        for (var i = 0; i < 20; i++) {
            var page = await f.Store.ListPendingScopesAsync(1, cursor); Assert.IsTrue(page.Scopes.Count <= 1);
            foreach (var item in page.Scopes) { Assert.AreEqual(f.Scope.RealmId, item.RealmId); scopes.Add(item.TenantId); }
            cursor = page.Continuation; if (cursor is null) break;
        }
        Assert.IsTrue(scopes.SetEquals([f.Native.Tenant, f.Native.OtherTenant]));
    }
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task CancellationAndTimeoutBoundAnUncooperativeReceiverWithoutLosingTheOriginalEvent(bool cancelCaller)
    {
        await using var f = await Fixture.Create(); await f.Change(false, Guid.NewGuid()); f.Settings.DeliveryTimeoutSeconds = 1;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Receiver.Before = _ => { entered.TrySetResult(); return release.Task; };
        using var cancel = new CancellationTokenSource(); var work = f.Dispatch.RunAsync(f.Scope, 1, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (cancelCaller) { cancel.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => work); }
        else await work.WaitAsync(TimeSpan.FromSeconds(5));
        var row = (await f.Store.ReadFirstUndeliveredAsync(f.Scope))!; Assert.AreEqual("pending", row.State); Assert.AreEqual(cancelCaller ? "cancelled" : "timeout", row.FailureCode);
        release.TrySetResult(); Assert.AreEqual(1, f.Receiver.Calls);
    }
    [TestMethod]
    public async Task CompetingNativeMutationsCannotCommitTheSameTenantSequenceOrAPhantomEvent()
    {
        await using var f = await Fixture.Create(); var role = await f.Roles.CreateRoleAsync(f.Native.Tenant, "Member");
        var firstOperation = Guid.NewGuid(); var secondOperation = Guid.NewGuid();
        f.Native.Storage.Match = action => action.ActionType == TableTransactionActionType.Add && action.Entity.RowKey.StartsWith("!IBEAM|MEMBERSHIP|EVENT|", StringComparison.Ordinal);
        var first = f.Change(false, firstOperation); await f.Native.Storage.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try {
            var current = (await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin))!;
            var desired = (await f.Roles.GetRolesForUserAsync(f.Native.Tenant, f.Native.OtherAdmin)).Append(role).ToArray();
            await f.Admin.SetRolesAsync(f.Native.Tenant, f.Native.OtherAdmin, desired, current.Version, secondOperation, f.Administrators);
        } finally { f.Native.Storage.Release(); }
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => first);
        Assert.IsNull(await f.Store.FindByOperationAsync(f.Scope, firstOperation)); Assert.IsNotNull(await f.Store.FindByOperationAsync(f.Scope, secondOperation));
        var events = await f.Store.ReadPageAsync(f.Scope, f.InitialSequence, 100); Assert.HasCount(1, events.Events); Assert.AreEqual(f.InitialSequence + 1, events.NextSequence);
        Assert.IsTrue((await f.Members.FindStateAsync(f.Native.Tenant, f.Native.OtherAdmin))!.IsEnabled);
    }
    [TestMethod]
    public async Task RejectedRoleGrantAfterCommittedLinkRecoversWithoutPublishingADuplicateLink()
    {
        await using var f = await Fixture.Create(); var role = await f.Roles.CreateRoleAsync(f.Native.Tenant, "Member"); var user = Guid.NewGuid();
        f.Native.Storage.FailEventBeforeCommit = true;
        f.Native.Storage.EventFault = actions => actions.Any(x => x.ActionType == TableTransactionActionType.Add && x.Entity is TableEntity row &&
            row.RowKey.StartsWith("!IBEAM|MEMBERSHIP|EVENT|", StringComparison.Ordinal) && row.GetString("EventJson")!.Contains("roles-granted", StringComparison.Ordinal));
        var request = new TenantMembershipRoleBootstrapRequest(f.Native.Tenant, user, "PRIVATE-TENANT", RoleNames: [role.Name]);
        await Assert.ThrowsAsync<IdentityProviderException>(() => f.Roles.EnsureTenantMembershipAndGrantRolesAsync(request));
        var linked = await f.Store.ReadPageAsync(f.Scope, f.InitialSequence, 100); Assert.HasCount(1, linked.Events); Assert.AreEqual("membership-linked", linked.Events[0].Kind);
        Assert.HasCount(0, linked.Events[0].Changes[0].After!.RoleIds); Assert.HasCount(0, await f.Roles.GetRolesForUserAsync(f.Native.Tenant, user));
        await f.Roles.EnsureTenantMembershipAndGrantRolesAsync(request);
        var recovered = await f.Store.ReadPageAsync(f.Scope, f.InitialSequence, 100); Assert.HasCount(2, recovered.Events);
        Assert.AreEqual(linked.Events[0].EventId, recovered.Events[0].EventId); Assert.AreEqual("roles-granted", recovered.Events[1].Kind);
    }
    [TestMethod]
    public async Task CorruptPayloadIsNotDeliveredAndProviderTextCannotEscapeTheDispatchBoundary()
    {
        await using var f = await Fixture.Create(); await f.Change(false, Guid.NewGuid()); var entry = (await f.Store.ReadFirstUndeliveredAsync(f.Scope))!;
        var row = new TableEntity("TEN|" + f.Native.Tenant.ToString("D"), "!IBEAM|MEMBERSHIP|EVENT|" + entry.Event.Sequence.ToString("D20")) { ["EventJson"] = "PRIVATE-CORRUPT-PAYLOAD" };
        await f.Native.Table("TenantUsers").UpdateEntityAsync(row, new Azure.ETag(entry.Version), TableUpdateMode.Merge);
        var error = await Assert.ThrowsAsync<IdentityProviderException>(() => f.Dispatch.RunAsync(f.Scope));
        Assert.DoesNotContain("PRIVATE-", error.ToString()); Assert.AreEqual(0, f.Receiver.Calls);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public IdentityPurgeFixture Native = null!; public AzureTableIdentityMembershipEventOutboxStore Store = null!;
        public AzureTableTenantMembershipStore Members = null!; public AzureTableTenantRoleStore Roles = null!;
        public IdentityTenantMembershipAdministrationService Admin = null!; public Guid[] Administrators = [];
        public IdentityMembershipEventScope Scope = null!; public IdentityMembershipEventDispatcher Dispatch = null!;
        public Receiver Receiver { get; } = new(); public Clock Clock { get; } = new(); public long InitialSequence;
        public IdentityMembershipEventDispatchOptions Settings { get; } = new() { Enabled = true, MaximumAttempts = 3, RetryDelaySeconds = 1 };
        private IServiceScope services = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture { Native = await IdentityPurgeFixture.Create(membershipEvents: true) };
            f.services = f.Native.Provider.CreateScope(); var p = f.services.ServiceProvider;
            f.Members = p.GetRequiredService<AzureTableTenantMembershipStore>(); f.Roles = p.GetRequiredService<AzureTableTenantRoleStore>();
            f.Administrators = (await f.Roles.GetRolesAsync(f.Native.Tenant)).Where(x => x.Name == "Administrator").Select(x => x.RoleId).ToArray();
            f.Admin = new(f.Members, f.Roles, p.GetRequiredService<AzureTableIdentityTenantStore>(), new ServiceOperationExecutor());
            f.Store = new(f.Native.Storage, Options.Create(f.Native.NativeOptions));
            f.Scope = new("fixture", "Testing", f.Native.Execution.Authority.RealmId, f.Native.Tenant);
            f.Clock.Now = DateTimeOffset.UtcNow.AddMinutes(1);
            f.Dispatch = new(f.Store, f.Receiver, Options.Create(f.Settings), f.Clock, new ServiceOperationExecutor());
            await f.Dispatch.RunAsync(f.Scope, 100); f.Receiver.Calls = 0; f.Receiver.Effects.Clear();
            f.InitialSequence = (await f.Store.ReadPageAsync(f.Scope, 0, 100)).NextSequence; return f;
        }
        public async Task Change(bool enabled, Guid operation)
        {
            using var actor = IdentityMembershipEventActorContext.Push(new(IdentityMembershipActorKind.Customer, Native.User, null, null, Guid.NewGuid()));
            var current = (await Members.FindStateAsync(Native.Tenant, Native.OtherAdmin))!;
            await Admin.SetEnabledAsync(Native.Tenant, Native.OtherAdmin, enabled, current.Version, operation, Administrators);
        }
        public async ValueTask DisposeAsync() { services.Dispose(); await Native.DisposeAsync(); }
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Receiver : IIdentityMembershipEventDelivery
    {
        public int ProtocolVersion => 1; public int Calls, FailuresRemaining; public Func<int, Task>? Before;
        public ConcurrentDictionary<Guid, byte> Effects { get; } = new();
        public async Task DeliverAsync(TenantMembershipChangedEvent committed, CancellationToken ct = default)
        { var call = Interlocked.Increment(ref Calls); if (Before is not null) await Before(call); Effects.TryAdd(committed.EventId, 0);
            if (FailuresRemaining-- > 0) throw new IOException("PRIVATE-CONSUMER-ERROR"); }
    }
}
