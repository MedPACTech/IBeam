using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services;
using IBeam.Management.Identity.Target.Services.Invitations;
using IBeam.Management.Operations;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass, TestCategory("StorageIntegration")]
public sealed class BulkInvitationRevokeTests
{
    [TestMethod]
    public async Task NativeBatchRevokesWithOnlyIndependentBulkGrantAndDurableOriginalItemIds()
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite); var result = await f.Execute(request);
        Assert.AreEqual("succeeded", result.State); Assert.IsTrue(result.Complete); Assert.AreEqual("revoked", result.Items.Single().State);
        var row = (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!;
        Assert.AreEqual(result.Items[0].OperationId, row.LastTransitionOperationId); Assert.IsNull(row.RevokedByUserId); Assert.IsNull(row.LastResendUtc);
        var version = row.Version; var replay = await f.Restart().ExecuteAsync(f.Scope, request);
        Assert.AreEqual(result.Items[0].OperationId, replay.Items[0].OperationId); Assert.AreEqual(1, f.Transitions.Calls);
        Assert.AreEqual(version, (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!.Version); Assert.AreEqual(1, await f.JournalRows());
        Assert.IsTrue((await f.Native.Table("AspNetUsers").GetEntityIfExistsAsync<TableEntity>("GLOBAL", f.Native.User.ToString("D"))).HasValue);
    }
    [TestMethod]
    public async Task BoundedAttemptsResumeTheOriginalBatchAndReadonlyPollsDoNotDoWork()
    {
        await using var f = await Fixture.Create(); f.Settings.MaximumItemsPerAttempt = 1; var second = await f.Add(); var third = await f.Add();
        var request = await f.Selection(f.Native.Invite, second, third); var first = await f.Execute(request);
        Assert.IsFalse(first.Complete); Assert.AreEqual("partial", first.State); Assert.AreEqual(1, f.Transitions.Calls);
        var originalIds = first.Items.Select(x => x.OperationId).ToArray();
        _ = await f.Restart().ReadReceiptAsync(f.Scope with { ResourceVersion = null }); Assert.AreEqual(1, f.Transitions.Calls);
        await f.Restart().ExecuteAsync(f.Scope, request); var last = await f.Restart().ExecuteAsync(f.Scope, request);
        Assert.IsTrue(last.Complete); Assert.AreEqual("succeeded", last.State); CollectionAssert.AreEqual(originalIds, last.Items.Select(x => x.OperationId).ToArray());
        Assert.AreEqual(3, f.Transitions.Calls); Assert.AreEqual(1, await f.JournalRows());
    }
    [TestMethod]
    [DataRow("accepted")] [DataRow("already-revoked")] [DataRow("missing")] [DataRow("stale")]
    public async Task IneligibleOrChangedItemsReportSafeOutcomesWithoutNativeMutation(string condition)
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite);
        if (condition == "accepted") { var current = (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!; await f.Invites.BeginAcceptanceAsync(f.Native.Tenant, f.Native.Invite, current.TokenHash, f.Native.User); }
        if (condition == "already-revoked") await f.Invites.TransitionAsync(new() { TenantId = f.Native.Tenant, InviteId = f.Native.Invite, OperationId = Guid.NewGuid(), Kind = "revoke", ExpectedVersion = request.Invitations[0].ExpectedVersion, NowUtc = DateTimeOffset.UtcNow });
        if (condition == "missing") request = new([new(Guid.NewGuid(), "version")]);
        if (condition == "stale") request = new([request.Invitations[0] with { ExpectedVersion = "stale" }]);
        var result = await f.Execute(request); Assert.IsTrue(result.Complete); Assert.AreEqual(0, f.Transitions.Calls);
        Assert.AreEqual(condition switch { "already-revoked" => "already-revoked", "stale" => "changed-version", _ => "not-eligible" }, result.Items[0].State);
        Assert.DoesNotContain("PRIVATE-", System.Text.Json.JsonSerializer.Serialize(result));
    }
    [TestMethod]
    public async Task NativeAcceptanceWinningAtTheCasBoundaryCannotBeRevoked()
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite);
        f.Transitions.Before = async (command, _) => { var current = (await f.Invites.GetAsync(command.TenantId, command.InviteId))!; await f.Invites.BeginAcceptanceAsync(command.TenantId, command.InviteId, current.TokenHash, f.Native.User); };
        var result = await f.Execute(request); Assert.AreEqual("accepted", result.Items[0].FailureCode); Assert.AreEqual("not-eligible", result.Items[0].State);
        var current = (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!; Assert.AreEqual(f.Native.User, current.AcceptanceUserId); Assert.AreNotEqual("revoked", current.Status);
    }
    [TestMethod]
    [DataRow("before-native")] [DataRow("after-native")] [DataRow("after-checkpoint")]
    public async Task ProviderOrCheckpointLostRepliesRecoverWithoutAnotherBatchOrNativeEffect(string boundary)
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite); var once = 0;
        if (boundary == "before-native") f.Transitions.Before = (_, _) => throw new IOException("PRIVATE-PROVIDER");
        if (boundary == "after-native") f.Transitions.After = (_, _) => throw new IOException("PRIVATE-COMMIT-REPLY");
        if (boundary == "after-checkpoint") f.Journal.AfterReplace = row => { if (row.Items.Any(x => x.State == "revoked") && Interlocked.Increment(ref once) == 1) throw new IOException("PRIVATE-CHECKPOINT-REPLY"); };
        var first = await f.Execute(request); Assert.IsFalse(first.Complete); Assert.AreEqual("unknown", first.State);
        var nativeVersion = (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!.Version;
        var calls = f.Transitions.Calls; var receipt = await f.Restart().ReadReceiptAsync(f.Scope with { ResourceVersion = null }); Assert.AreEqual(calls, f.Transitions.Calls);
        f.Transitions.Before = null; f.Transitions.After = null; f.Journal.AfterReplace = null;
        var recovered = await f.Restart().ExecuteAsync(f.Scope, request); Assert.IsTrue(recovered.Complete); Assert.AreEqual("revoked", recovered.Items[0].State);
        Assert.AreEqual(first.Items[0].OperationId, recovered.Items[0].OperationId); Assert.AreEqual(1, await f.JournalRows());
        if (boundary != "before-native") Assert.AreEqual(nativeVersion, (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!.Version);
        Assert.DoesNotContain("PRIVATE-", System.Text.Json.JsonSerializer.Serialize(receipt));
    }
    [TestMethod]
    public async Task RevokedBulkGrantStopsTheNextItemAndDeniesPollsUntilAuthorizedAgain()
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite, await f.Add());
        f.Transitions.After = (_, _) => { f.Granted = false; return Task.CompletedTask; };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Execute(request)); Assert.AreEqual(1, f.Transitions.Calls);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Restart().ReadReceiptAsync(f.Scope));
        f.Granted = true; f.Transitions.After = null; var done = await f.Restart().ExecuteAsync(f.Scope, request);
        Assert.IsTrue(done.Complete); Assert.AreEqual(2, f.Transitions.Calls); Assert.AreEqual(1, await f.JournalRows());
    }
    [TestMethod]
    public async Task RateLimitLeavesResumablePartialProgressWithoutResendingMail()
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite, await f.Add());
        f.Transitions.Before = (_, _) => throw new ManagementCustomerIdentityRateLimitException();
        var partial = await f.Execute(request); Assert.IsFalse(partial.Complete); Assert.AreEqual("rate-limited", partial.Items.Single(x => x.State == "failed").FailureCode);
        f.Transitions.Before = null; var complete = await f.Restart().ExecuteAsync(f.Scope, request); Assert.IsTrue(complete.Complete);
        Assert.IsNull((await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!.LastResendUtc);
    }
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task DeadlineOrCallerCancellationLeavesOriginalResumableUnknownIntent(bool callerCancellation)
    {
        await using var f = await Fixture.Create(); f.Settings.AttemptSeconds = 1; var request = await f.Selection(f.Native.Invite);
        f.Transitions.Before = (_, ct) => Task.Delay(Timeout.Infinite, ct); using var cancel = new CancellationTokenSource();
        if (callerCancellation) cancel.CancelAfter(100);
        if (callerCancellation) await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.ExecuteAsync(f.Scope, request, cancel.Token));
        else { var result = await f.Service.ExecuteAsync(f.Scope, request); Assert.AreEqual("unknown", result.State); }
        var receipt = await f.Restart().ReadReceiptAsync(f.Scope); Assert.AreEqual("unknown", receipt.State);
        f.Transitions.Before = null; var done = await f.Restart().ExecuteAsync(f.Scope, request); Assert.IsTrue(done.Complete); Assert.AreEqual(1, await f.JournalRows());
    }
    [TestMethod]
    public async Task SameIntentCannotChangeSelectionVersionOrOriginalSupportPerson()
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite); await f.Execute(request);
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Execute(new([new(Guid.NewGuid(), "version")])));
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Service.ExecuteAsync(f.Scope with { ResourceVersion = "changed" }, request));
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Service.ReadReceiptAsync(f.Scope with { ActorUserId = Guid.NewGuid() })); Assert.AreEqual(1, f.Transitions.Calls);
    }
    [TestMethod]
    [DataRow("app")] [DataRow("tenant")] [DataRow("realm")]
    public async Task ScopeMismatchesCannotReadOriginalItems(string field)
    {
        await using var f = await Fixture.Create(); await f.Execute(await f.Selection(f.Native.Invite));
        var wrong = field switch { "app" => f.Scope with { ApplicationId = Guid.NewGuid() }, "tenant" => f.Scope with { TenantId = f.Native.OtherTenant }, _ => f.Scope with { RealmId = Guid.NewGuid() } };
        if (field == "realm") await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.ReadReceiptAsync(wrong));
        else Assert.AreEqual(0, (await f.Service.ReadReceiptAsync(wrong)).Total);
    }
    [TestMethod]
    [DataRow(0)] [DataRow(51)]
    public async Task EmptyOrOversizeSelectionCannotReserveAJob(int size)
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Execute(new(Enumerable.Range(0, size).Select(_ => new ManagementBulkInvitationSelection(Guid.NewGuid(), "version")).ToArray())));
        Assert.AreEqual(0, await f.JournalRows()); Assert.AreEqual(0, f.Transitions.Calls);
    }
    [TestMethod]
    public async Task DuplicateIdsWildcardAndStaleTenantVersionsAreRejectedBeforeWork()
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite);
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Execute(new([request.Invitations[0], request.Invitations[0]])));
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Execute(new([request.Invitations[0] with { ExpectedVersion = "*" }])));
        await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Service.ExecuteAsync(f.Scope with { ResourceVersion = "stale" }, request));
        Assert.AreEqual(0, await f.JournalRows()); Assert.AreEqual(0, f.Transitions.Calls);
    }
    [TestMethod]
    public async Task ConcurrentResumeUsesOneJournalAndOnlyTheOriginalNativeTransitionIntent()
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite);
        await Task.WhenAll(f.Execute(request), f.Restart().ExecuteAsync(f.Scope, request));
        var result = await f.Service.ReadReceiptAsync(f.Scope); Assert.IsTrue(result.Complete); Assert.AreEqual(1, await f.JournalRows());
        Assert.IsTrue(f.Transitions.Ids.All(x => x == result.Items.Single().OperationId));
    }
    [TestMethod]
    public async Task ExpiredReceiptDoesNotCreateAnotherBatchOrRepeatNativeWork()
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite); await f.Execute(request); f.Clock.Now = f.Clock.Now.AddDays(8);
        await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Service.ReadReceiptAsync(f.Scope));
        await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Execute(request)); Assert.AreEqual(1, f.Transitions.Calls); Assert.AreEqual(1, await f.JournalRows());
    }
    [TestMethod]
    public async Task NativePurgeFencesPreventNewBulkMutationOrTenantRecreation()
    {
        await using var f = await Fixture.Create(); var request = await f.Selection(f.Native.Invite); await f.Native.Confirm(); await f.Native.Complete();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Execute(request)); Assert.AreEqual(0, f.Transitions.Calls); Assert.AreEqual(0, await f.JournalRows());
    }
    [TestMethod]
    public async Task FiftySelectedOpaqueVersionsFitTheDurableJournalWithoutExceedingPropertyBounds()
    {
        await using var f = await Fixture.Create(); var opaque = new string('\u4f60', 256);
        var request = new ManagementBulkInvitationRevokeRequest(Enumerable.Range(0, 50).Select(_ => new ManagementBulkInvitationSelection(Guid.NewGuid(), opaque)).ToArray());
        var result = await f.Execute(request); Assert.AreEqual(50, result.Total); Assert.IsFalse(result.Complete); Assert.AreEqual(0, f.Transitions.Calls);
        await foreach (var row in f.Table.QueryAsync<TableEntity>()) { Assert.IsTrue(row.GetBinaryData("Data0")!.ToArray().Length <= 60000); Assert.IsTrue(row.GetBinaryData("Data1")!.ToArray().Length > 0); }
        Assert.DoesNotContain(opaque, System.Text.Json.JsonSerializer.Serialize(result)); Assert.AreEqual(50, (await f.Restart().ReadReceiptAsync(f.Scope)).Total);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public IdentityPurgeFixture Native = null!; public AzureTableTenantInviteStore Invites = null!; public ManagementOperationContext Scope = null!;
        public TableClient Table = null!; public FaultJournal Journal = null!; public TransitionGate Transitions = null!;
        public ManagementBulkInvitationRevokeService Service = null!; public bool Granted = true; public Clock Clock { get; } = new();
        public ManagementBulkInvitationRevokeOptions Settings { get; } = new() { Enabled = true };
        private IServiceScope services = null!; private readonly Mock<IManagementOperationAccessService> access = new();
        public static async Task<Fixture> Create()
        {
            var f = new Fixture { Native = await IdentityPurgeFixture.Create() }; f.services = f.Native.Provider.CreateScope();
            f.Invites = f.services.ServiceProvider.GetRequiredService<AzureTableTenantInviteStore>(); f.Transitions = new(f.Invites);
            f.Table = f.Native.Table("BulkInvitations"); await f.Table.CreateIfNotExistsAsync(); f.Journal = new(new AzureTableManagementBulkInvitationBatchRepository(f.Table));
            var version = (await f.services.ServiceProvider.GetRequiredService<AzureTableIdentityTenantStore>().FindVersionedAsync(f.Native.Tenant))!.Version;
            f.Scope = f.Native.Execution.Authority with { OperationId = Guid.NewGuid(), ResourceId = Guid.NewGuid(), ResourceVersion = version };
            f.access.SetupGet(x => x.ProtocolVersion).Returns(1);
            f.access.Setup(x => x.RequireAsync(It.IsAny<string>(), It.IsAny<ManagementOperationContext>(), It.IsAny<CancellationToken>())).Returns<string, ManagementOperationContext, CancellationToken>((op, _, ct) => {
                ct.ThrowIfCancellationRequested(); if (!f.Granted || op != ManagementBulkInvitationRevokeOperation.Name) throw new ManagementAccessDeniedException(); return Task.CompletedTask;
            });
            f.Service = f.Restart(); return f;
        }
        public ManagementBulkInvitationRevokeService Restart() => Service = new(Journal, Invites, Transitions, services.ServiceProvider.GetRequiredService<AzureTableIdentityTenantStore>(), access.Object,
            Options.Create(Settings), Options.Create(new ManagementCustomerIdentityTargetOptions { IdentityRealmId = Scope.RealmId, DedicatedApplicationRealm = true }),
            Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = "fixture", Environment = "Testing" }), Clock, new ServiceOperationExecutor());
        public Task<ManagementBulkInvitationRevokeResult> Execute(ManagementBulkInvitationRevokeRequest request) => Service.ExecuteAsync(Scope, request);
        public async Task<Guid> Add() { var id = Guid.NewGuid(); await Invites.CreateAsync(Native.Invitation() with { InviteId = id, TokenHash = "synthetic-" + id.ToString("N") }); return id; }
        public async Task<ManagementBulkInvitationRevokeRequest> Selection(params Guid[] ids) => new(await Task.WhenAll(ids.Select(async id => new ManagementBulkInvitationSelection(id, (await Invites.GetAsync(Native.Tenant, id))!.Version!))));
        public async Task<int> JournalRows() { var count = 0; await foreach (var _ in Table.QueryAsync<TableEntity>()) count++; return count; }
        public async ValueTask DisposeAsync() { services.Dispose(); if (!Table.Uri.IsLoopback || !Table.Name.EndsWith("BulkInvitations", StringComparison.Ordinal)) throw new InvalidOperationException(); await Table.DeleteAsync(); await Native.DisposeAsync(); }
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class TransitionGate(IManagedTenantInviteStore inner) : IManagedTenantInviteStore
    {
        public int Calls; public System.Collections.Concurrent.ConcurrentBag<Guid> Ids { get; } = [];
        public Func<ManagedTenantInviteTransition, CancellationToken, Task>? Before, After;
        public Task<TenantInviteRecord> BeginAcceptanceAsync(Guid tenant, Guid invite, string hash, Guid user, CancellationToken ct = default) => inner.BeginAcceptanceAsync(tenant, invite, hash, user, ct);
        public async Task<TenantInviteRecord> TransitionAsync(ManagedTenantInviteTransition request, CancellationToken ct = default) {
            Interlocked.Increment(ref Calls); Ids.Add(request.OperationId); if (Before is not null) await Before(request, ct);
            var result = await inner.TransitionAsync(request, ct); if (After is not null) await After(request, ct); return result;
        }
    }
    private sealed class FaultJournal(IManagementBulkInvitationBatchRepository inner) : IManagementBulkInvitationBatchRepository
    {
        public int ProtocolVersion => inner.ProtocolVersion; public bool Durable => inner.Durable; public Action<ManagementBulkInvitationBatch>? AfterReplace;
        public Task<ManagementBulkInvitationBatch?> GetAsync(ManagementOperationContext scope, CancellationToken ct = default) => inner.GetAsync(scope, ct);
        public Task<bool> TryCreateAsync(ManagementBulkInvitationBatch row, CancellationToken ct = default) => inner.TryCreateAsync(row, ct);
        public async Task<ManagementBulkInvitationBatch> ReplaceAsync(ManagementBulkInvitationBatch row, string version, CancellationToken ct = default) { var saved = await inner.ReplaceAsync(row, version, ct); AfterReplace?.Invoke(saved); return saved; }
    }
}
