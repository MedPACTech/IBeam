using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Target.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
[TestCategory("StorageIntegration")]
public sealed class DurablePurgeTests
{
    [TestMethod]
    public async Task SameJobConfirmationRaceRecoversItsReceiptAfterAStaleInitialRead()
    {
        await using var f = await Fixture.Create(); await f.Confirm(); var reads = 0;
        var repository = new Mock<IManagementTenantPurgeJournalRepository>();
        repository.Setup(x => x.GetJobAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<CancellationToken>()))
            .Returns((ManagementTenantPurgeExecution e, CancellationToken ct) => Interlocked.Increment(ref reads) == 1 ? Task.FromResult<ManagementTenantPurgeJournalEntity?>(null) : f.Store.GetJobAsync(e, ct));
        repository.Setup(x => x.GetPlanAsync(It.IsAny<ManagementTenantPurgeExecution>(), f.SavedPlan!.PlanId, It.IsAny<CancellationToken>()))
            .Returns((ManagementTenantPurgeExecution e, Guid id, CancellationToken ct) => f.Store.GetPlanAsync(e, id, ct));
        var receipt = await f.JobsFor(repository.Object).ConfirmAsync(f.Execution, f.SavedPlan!);
        Assert.AreEqual("accepted", receipt.Outcome); Assert.AreEqual(f.Execution.JobId, receipt.Job!.JobId); Assert.AreEqual(2, reads);
        repository.Verify(x => x.TryConfirmAsync(It.IsAny<ManagementTenantPurgeJournalEntity>(), It.IsAny<string>(), It.IsAny<ManagementTenantPurgeJournalEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(await f.Native.DataExists()); Assert.AreEqual(0, f.Native.PurgeCalls);
    }
    [TestMethod]
    public async Task ConcurrentConfirmationConsumesOnePlanAndReservesOneJobWithoutInlineDeletion()
    {
        await using var f = await Fixture.Create(); var plan = await f.Plan();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Jobs.ConfirmAsync(f.Execution, plan)));
        Assert.IsTrue(results.All(x => x.Job!.JobId == f.Execution.JobId)); Assert.IsTrue(await f.Native.DataExists());
        var stored = await f.Store.GetPlanAsync(f.Execution, plan.PlanId); Assert.AreEqual(f.Execution.JobId, stored!.ConsumedJobId);
        var other = f.WithJob(Guid.NewGuid());
        await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Jobs.ConfirmAsync(other, plan));
        await f.Worker.RunAsync(f.Execution);
        Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State); Assert.IsFalse(await f.Native.DataExists());
        Assert.IsTrue(await f.Native.OtherDataExists()); Assert.IsTrue(await f.Native.GlobalDataExists());
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Native.CustomerAccessOrWrite());
        f.Rebuild(); await f.Worker.RunAsync(f.Execution);
        Assert.AreEqual(1, await f.Native.Commits());
    }
    [TestMethod]
    [DataRow("fencing")] [DataRow("purging")] [DataRow("verifying")]
    public async Task LostNativeRepliesPauseAndRestartReconcilesOnlyTheSameScopedJob(string phase)
    {
        await using var f = await Fixture.Create(); await f.Confirm(); f.Native.LoseReply = phase;
        await f.Worker.RunAsync(f.Execution);
        var row = await f.Store.GetJobAsync(f.Execution); Assert.IsTrue(row!.NeedsReconciliation); Assert.AreEqual("unknown", row.State);
        Assert.AreEqual(phase, row.Phase); Assert.AreEqual("unknown", row.FailureCode);
        var oldCalls = f.Native.PurgeCalls; await f.Worker.RunAsync(f.Execution); Assert.AreEqual(oldCalls, f.Native.PurgeCalls);
        f.Rebuild(); await f.Worker.RunAsync(f.Execution); Assert.IsTrue((await f.Store.GetJobAsync(f.Execution))!.NeedsReconciliation);
        await f.Worker.RunAsync(f.Execution, reconcileUncertain: true);
        Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State); Assert.AreEqual(1, await f.Native.Commits());
        Assert.IsTrue(await f.Native.OtherDataExists());
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(await f.Jobs.FindAsync(f.Execution)) + JsonSerializer.Serialize(f.Audits));
        Assert.IsTrue(f.Audits.All(x => x.SubjectType == "system" && x.SubjectId == f.Execution.JobId));
    }
    [TestMethod]
    public async Task ACrashMarkerAndLeaseLossPreventAnOldWorkerFromCommitting()
    {
        await using var f = await Fixture.Create(); await f.Confirm();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Native.BeforePurge = async () => { entered.SetResult(); await release.Task; };
        var old = f.Worker.RunAsync(f.Execution); await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var during = await f.Store.GetJobAsync(f.Execution); Assert.IsTrue(during!.NeedsReconciliation);
        await f.Worker.RunAsync(f.Execution); Assert.AreEqual(1, f.Native.PurgeCalls);
        f.Clock.Now = f.Clock.Now.AddSeconds(31); f.Rebuild();
        await f.Worker.RunAsync(f.Execution); Assert.AreEqual(0, f.Native.PurgeCalls);
        await f.Worker.RunAsync(f.Execution, reconcileUncertain: true); release.SetResult(); await old;
        Assert.AreEqual(1, await f.Native.Commits()); Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
    }
    [TestMethod]
    public async Task CancellationIsAllowedOnlyBeforeAnyFenceAttemptAndNeverRestoresAccess()
    {
        await using (var f = await Fixture.Create()) {
            await f.Confirm(); await f.Worker.CancelBeforeFenceAsync(f.Execution); await f.Worker.RunAsync(f.Execution);
            Assert.AreEqual("cancelled", (await f.Jobs.FindAsync(f.Execution)).Job!.State); Assert.IsTrue(await f.Native.DataExists());
            await f.Native.CustomerAccessOrWrite();
        }
        await using (var f = await Fixture.Create()) {
            await f.Confirm(); f.Native.LoseReply = "fencing"; await f.Worker.RunAsync(f.Execution);
            await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Worker.CancelBeforeFenceAsync(f.Execution));
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Native.CustomerAccessOrWrite());
            Assert.IsTrue(await f.Native.DataExists());
        }
    }
    [TestMethod]
    [DataRow("partial")] [DataRow("missing")] [DataRow("pending")]
    public async Task IncompleteCoverageCannotBecomeSuccess(string mode)
    {
        await using var f = await Fixture.Create(); await f.Confirm(); f.Native.CoverageMode = mode;
        await f.Worker.RunAsync(f.Execution); var row = await f.Store.GetJobAsync(f.Execution);
        Assert.AreEqual(mode == "partial" ? "partially-completed" : "failed", row!.State);
        Assert.IsFalse(f.Audits.Any(x => x.Succeeded));
        if (mode == "partial") Assert.IsTrue(await f.Native.DataExists());
        else Assert.AreEqual("verification-failed", row.FailureCode);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Native.CustomerAccessOrWrite());
    }
    [TestMethod]
    public async Task VerificationRepairRequiresExplicitSameJobReconciliationAndDoesNotDuplicateNativeDeletion()
    {
        await using var f = await Fixture.Create(); await f.Confirm(); f.Native.CoverageMode = "missing";
        await f.Worker.RunAsync(f.Execution); Assert.AreEqual("failed", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
        f.Native.CoverageMode = "complete"; await f.Worker.RunAsync(f.Execution); Assert.AreEqual(1, f.Native.PurgeCalls);
        await f.Worker.RunAsync(f.Execution, reconcileUncertain: true);
        Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State); Assert.AreEqual(1, await f.Native.Commits());
    }
    [TestMethod]
    public async Task PositiveSameJobPendingMayResumeAutomaticallyButAChangedNativeRevisionCannotDelete()
    {
        await using (var f = await Fixture.Create()) {
            await f.Confirm(); f.Native.Pending = true; await f.Worker.RunAsync(f.Execution);
            Assert.IsFalse((await f.Store.GetJobAsync(f.Execution))!.NeedsReconciliation); Assert.IsTrue(await f.Native.DataExists());
            f.Native.Pending = false; await f.Worker.RunAsync(f.Execution); Assert.AreEqual(1, await f.Native.Commits());
        }
        await using (var f = await Fixture.Create()) {
            await f.Confirm(); await f.Native.ChangeRevision(); await f.Worker.RunAsync(f.Execution);
            var status = (await f.Jobs.FindAsync(f.Execution)).Job!;
            Assert.AreEqual("failed", status.State); Assert.AreEqual("fence-failed", status.FailureCode); Assert.IsTrue(await f.Native.DataExists());
        }
    }
    [TestMethod]
    [DataRow("actor")] [DataRow("tenant")] [DataRow("app")] [DataRow("org")] [DataRow("realm")] [DataRow("environment")]
    public async Task ForeignScopeCannotPollRunOrCancelTheJob(string change)
    {
        await using var f = await Fixture.Create(); await f.Confirm();
        var e = change switch {
            "actor" => f.Execution with { Authority = f.Execution.Authority with { ActorUserId = Guid.NewGuid() } },
            "tenant" => f.Execution with { Authority = f.Execution.Authority with { TenantId = Guid.NewGuid(), ResourceId = Guid.NewGuid() } },
            "app" => f.Execution with { Authority = f.Execution.Authority with { ApplicationId = Guid.NewGuid() } },
            "org" => f.Execution with { Authority = f.Execution.Authority with { OrganizationId = Guid.NewGuid() } },
            "realm" => f.Execution with { Authority = f.Execution.Authority with { RealmId = Guid.NewGuid() } }, _ => f.Execution with { Environment = "Production" }
        };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Jobs.FindAsync(e));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Worker.RunAsync(e));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Worker.CancelBeforeFenceAsync(e));
        Assert.IsTrue(await f.Native.DataExists());
    }
    [TestMethod]
    public async Task RevocationDeniesNewCommandsAndPollingWhileAcceptedWorkKeepsItsOriginalSystemAttribution()
    {
        await using var f = await Fixture.Create(); await f.Confirm(); f.SupportEnabled = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Jobs.FindAsync(f.Execution));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Jobs.ConfirmAsync(f.WithJob(Guid.NewGuid()), f.SavedPlan!));
        await f.Worker.RunAsync(f.Execution); Assert.AreEqual(1, await f.Native.Commits());
        f.SupportEnabled = true; Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
        f.Target.EnabledOperations = []; await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Worker.RunAsync(f.Execution));
    }
    [TestMethod]
    public async Task MissingOrChangedNativeFencesAndExpiredPlansCannotStartDestruction()
    {
        await using (var f = await Fixture.Create()) {
            var plan = await f.Plan(); f.Clock.Now = plan.ExpiresUtc;
            await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Jobs.ConfirmAsync(f.Execution, plan));
            Assert.IsNull(await f.Store.GetJobAsync(f.Execution));
        }
        await using (var f = await Fixture.Create()) {
            var plan = await f.Plan(); f.Fences = []; f.Rebuild(replaceNative: false);
            await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => f.Jobs.ConfirmAsync(f.Execution, plan));
            Assert.IsTrue(await f.Native.DataExists());
        }
        await using (var f = await Fixture.Create()) {
            await f.Confirm(); f.Fences = []; f.Rebuild(replaceNative: false);
            await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => f.Worker.RunAsync(f.Execution));
            Assert.IsTrue(await f.Native.DataExists());
        }
    }
    [TestMethod]
    public async Task ExactConfirmationReplayReadsTheOriginalReceiptAfterVersionExpiryAndInventoryChange()
    {
        await using var f = await Fixture.Create(); await f.Confirm(); await f.Worker.RunAsync(f.Execution);
        f.Clock.Now = f.Clock.Now.AddMinutes(5); f.Native.InventoryVersion = 2;
        var request = new ManagementTenantPurgeRequest("confirm", "enterprise", "v1", f.SavedPlan!.PlanId, f.SavedPlan.ConfirmationBinding);
        var prior = await f.Boundary().ExecuteAsync(f.Execution.Authority, request);
        Assert.AreEqual("succeeded", prior.Job!.State); Assert.AreEqual(f.Execution.JobId, prior.Job.JobId); Assert.AreEqual(1, await f.Native.Commits());
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Boundary().ExecuteAsync(f.Execution.Authority, request with { ConfirmationBinding = new('A', 64) }));
        Assert.AreEqual("succeeded", (await f.Boundary().ReadReceiptAsync(f.Execution.Authority)).Job!.State);
    }
    [TestMethod]
    public async Task PlanReplayNeverExtendsTheOriginalExpiryAndChangedIntentConflicts()
    {
        await using var f = await Fixture.Create(); var original = await f.Plan(); var e = f.WithJob(original.PlanId);
        f.Clock.Now = f.Clock.Now.AddSeconds(10);
        var retry = original with { CreatedUtc = f.Clock.GetUtcNow(), ExpiresUtc = f.Clock.GetUtcNow().AddMinutes(3) };
        retry = retry with { ConfirmationBinding = ManagementTenantPurgeProtocol.Binding(e, retry) };
        var stored = await f.Jobs.CreatePlanAsync(e, retry);
        Assert.AreEqual(original.ExpiresUtc, stored.ExpiresUtc); Assert.AreEqual(original.ConfirmationBinding, stored.ConfirmationBinding);
        var changed = retry with { ExpectedVersion = "v2" }; changed = changed with { ConfirmationBinding = ManagementTenantPurgeProtocol.Binding(e, changed) };
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Jobs.CreatePlanAsync(e with { Authority = e.Authority with { ResourceVersion = "v2" } }, changed));
    }
    [TestMethod]
    public async Task AHostCapabilityDisabledAtTheNativeCommitBoundaryLeavesTheJobUncertainAndTheTenantFenced()
    {
        await using var f = await Fixture.Create(); await f.Confirm();
        f.Native.BeforePurge = () => { f.Target.EnabledOperations = []; return Task.CompletedTask; };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Worker.RunAsync(f.Execution));
        Assert.IsTrue(await f.Native.DataExists()); await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Native.CustomerAccessOrWrite());
        f.Target.EnabledOperations = [ManagementTenantDataOperations.Delete]; f.Clock.Now = f.Clock.Now.AddSeconds(31);
        Assert.AreEqual("unknown", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
        f.Native.BeforePurge = null; await f.Worker.RunAsync(f.Execution); Assert.IsTrue(await f.Native.DataExists());
        await f.Worker.RunAsync(f.Execution, reconcileUncertain: true); Assert.AreEqual(1, await f.Native.Commits());
    }
    [TestMethod]
    public async Task ExactEtagsFenceStaleJournalWritersAndMetadataScanIsScopedAndBounded()
    {
        await using var f = await Fixture.Create(); await f.Confirm(); var old = (await f.Store.GetJobAsync(f.Execution))!;
        await f.Store.ReplaceJobAsync(old with { UpdatedUtc = old.UpdatedUtc.AddSeconds(1) }, old.Version);
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Store.ReplaceJobAsync(old, old.Version));
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Store.ReplaceJobAsync(old, " * "));
        var s = f.Execution.Authority;
        var page = await f.Store.ListPageAsync(new("fixture", "Testing", s.OrganizationId, s.ApplicationId, s.RealmId), 1, null);
        Assert.HasCount(1, page.Items); Assert.AreEqual(f.Execution.JobId, page.Items[0].JobId);
        var foreign = await f.Store.ListPageAsync(new("fixture", "Testing", s.OrganizationId, Guid.NewGuid(), s.RealmId), 1, null); Assert.HasCount(0, foreign.Items);
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Store.ListPageAsync(new("fixture", "Testing", s.OrganizationId, s.ApplicationId, s.RealmId), 26, null));
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public TableClient JournalTable { get; private set; } = null!;
        public TableClient NativeTable { get; private set; } = null!;
        public AzureTableManagementTenantPurgeJournalRepository Store { get; private set; } = null!;
        public Clock Clock { get; } = new();
        public ManagementTenantPurgeExecution Execution { get; }
        public ManagementTenantPurgeJobService Jobs { get; private set; } = null!;
        public ManagementTenantPurgeWorker Worker { get; private set; } = null!;
        public NativeProvider Native { get; private set; } = null!;
        public IManagementTenantPurgeAccessFence[] Fences = [];
        public bool SupportEnabled = true;
        public ConcurrentBag<ManagementAuditEntity> Audits { get; } = [];
        public ManagementTenantPurgePlan? SavedPlan;
        public ManagementOperationTargetOptions Target { get; }
        private readonly Mock<IManagementOperationAccessService> access = new();
        private readonly Mock<IManagementAuditRepository> audit = new();
        private readonly ManagementDiagnosticsOptions deployment = new() { ApplicationKey = "fixture", Environment = "Testing" };
        private Fixture() {
            var tenant = Guid.NewGuid(); var job = Guid.NewGuid();
            Execution = new(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tenant, Guid.NewGuid(), job) { ResourceId = tenant, ResourceVersion = "v1" }, "fixture", "Testing", job, "enterprise", ManagementTenantPurgeProtocol.Fingerprint(NativeProvider.Definition));
            Target = new() { DedicatedApplicationRealm = true, RealmId = Execution.Authority.RealmId, EnabledOperations = [ManagementTenantDataOperations.Delete],
                TrustedPortals = [new() { OrganizationId = Execution.Authority.OrganizationId, ApplicationId = Execution.Authority.ApplicationId }] };
            access.SetupGet(x => x.ProtocolVersion).Returns(1);
            access.Setup(x => x.RequireAsync(ManagementTenantDataOperations.Delete, It.IsAny<ManagementOperationContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => SupportEnabled ? Task.CompletedTask : Task.FromException(new ManagementAccessDeniedException()));
            audit.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>())).Callback<ManagementAuditEntity, CancellationToken>((e, _) => Audits.Add(e)).Returns(Task.CompletedTask);
        }
        public static async Task<Fixture> Create() {
            var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
            if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("A private Table fixture is required.");
            var service = new TableServiceClient(connection); var f = new Fixture();
            f.JournalTable = service.GetTableClient("PurgeJournal" + Guid.NewGuid().ToString("N"));
            f.NativeTable = service.GetTableClient("PurgeNative" + Guid.NewGuid().ToString("N"));
            await f.JournalTable.CreateIfNotExistsAsync(); await f.NativeTable.CreateIfNotExistsAsync();
            f.Store = new(f.JournalTable); f.Rebuild(); await f.Native.Seed(); return f;
        }
        public void Rebuild(bool replaceNative = true) {
            if (replaceNative) { Native = new(NativeTable, Execution); Fences = [Native]; }
            var executor = new ServiceOperationExecutor(); var d = Options.Create(deployment); var t = Options.Create(Target);
            Jobs = new(Store, access.Object, Native, Native, Fences, Options.Create(new ManagementTenantPurgeOptions { Enabled = true, PolicyKeys = ["enterprise"] }), d, t, Clock, executor);
            Worker = new(Store, Native, Native, Fences, audit.Object, Options.Create(new ManagementAuditOptions()), Options.Create(new ManagementTenantPurgeJobOptions { LeaseSeconds = 30 }), Clock, executor, d, t);
        }
        public ManagementTenantPurgeJobService JobsFor(IManagementTenantPurgeJournalRepository repository) => new(repository, access.Object, Native, Native, Fences,
            Options.Create(new ManagementTenantPurgeOptions { Enabled = true, PolicyKeys = ["enterprise"] }), Options.Create(deployment), Options.Create(Target), Clock, new ServiceOperationExecutor());
        public ManagementTenantPurgeExecution WithJob(Guid job) => Execution with { JobId = job, Authority = Execution.Authority with { OperationId = job } };
        public async Task<ManagementTenantPurgePlan> Plan() {
            var e = WithJob(Guid.NewGuid()); var now = Clock.GetUtcNow();
            var p = new ManagementTenantPurgePlan(1, e.Authority.TenantId, e.JobId, "enterprise", "v1", e.InventoryFingerprint, "", now, now.AddMinutes(3), ManagementTenantPurgeProtocol.Definition(NativeProvider.Definition).Categories.ToArray());
            p = p with { ConfirmationBinding = ManagementTenantPurgeProtocol.Binding(e, p) }; SavedPlan = await Jobs.CreatePlanAsync(e, p); return SavedPlan;
        }
        public async Task Confirm() { var p = await Plan(); await Jobs.ConfirmAsync(Execution, p); }
        public ManagementTenantPurgeTarget Boundary() => new(access.Object, Jobs, Native, Native, Options.Create(deployment),
            Options.Create(new ManagementTenantPurgeOptions { Enabled = true, PolicyKeys = ["enterprise"] }), new ServiceOperationExecutor(), Clock);
        public async ValueTask DisposeAsync() {
            // Exact randomly named tables owned by this private fixture only; never arbitrary storage deletion.
            if (JournalTable is not null) await JournalTable.DeleteAsync(); if (NativeTable is not null) await NativeTable.DeleteAsync();
        }
    }
    internal sealed class NativeProvider(TableClient table, ManagementTenantPurgeExecution expected) : IManagementTenantPurgeHandler, IManagementTenantPurgeAccessFence, IManagementTenantPurgePolicy
    {
        public static ManagementTenantPurgeDefinition Definition => new(1, "fixture-native", 1, Array.AsReadOnly(new[] {
            new ManagementTenantPurgeCategory("app-data", "dataset", "delete"), new("audit-history", "dataset", "retain"), new("global-accounts", "identity-metadata", "exclude") }));
        public int ProtocolVersion => 1; public string Key => "fixture-app";
        public string? LoseReply; public bool Pending; public string CoverageMode = "complete"; public int PurgeCalls; public int InventoryVersion = 1;
        public Func<Task>? BeforePurge;
        private string Partition => expected.Authority.TenantId.ToString("N");
        public ManagementTenantPurgeDefinition Describe() => Definition with { InventoryVersion = InventoryVersion };
        public async Task Seed() {
            await table.AddEntityAsync(new TableEntity(Partition, "state") { ["Revision"] = "v1", ["Blocked"] = false });
            await table.AddEntityAsync(new TableEntity(Partition, "data") { ["Value"] = "PRIVATE-CUSTOMER-DATA" });
            await table.AddEntityAsync(new TableEntity("unrelated-tenant", "data") { ["Value"] = "PRIVATE-OTHER-TENANT" });
            await table.AddEntityAsync(new TableEntity("global-account", "credential") { ["Value"] = "PRIVATE-GLOBAL-CREDENTIAL" });
        }
        public async Task RequireAsync(ManagementTenantPurgeExecution e, string version, CancellationToken ct = default) {
            Check(e); var state = await table.GetEntityAsync<TableEntity>(Partition, "state", cancellationToken: ct);
            if (state.Value.GetString("Revision") != version || state.Value.GetBoolean("Blocked") == true) throw new ManagementOperationPreconditionException();
        }
        public Task RequireStatusAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) { Check(e); return Task.CompletedTask; }
        public Task PreviewAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) => RequireStatusAsync(e, ct);
        public async Task FenceOrReconcileAsync(ManagementTenantPurgeExecution e, string expectedVersion, CancellationToken ct = default) {
            Check(e); await e.LeaseFence!.RequireAsync(ct); var state = (await table.GetEntityAsync<TableEntity>(Partition, "state", cancellationToken: ct)).Value;
            if (state.GetBoolean("Blocked") == true) { if (state.GetString("Owner") != Scope(e)) throw new ManagementTenantPurgePermanentFailureException("fence-failed"); return; }
            if (state.GetString("Revision") != expectedVersion) throw new ManagementTenantPurgePermanentFailureException("fence-failed");
            state["Blocked"] = true; state["Owner"] = Scope(e); state["Revision"] = "fenced-v2";
            await e.LeaseFence.RequireAsync(ct); await table.UpdateEntityAsync(state, state.ETag, TableUpdateMode.Replace, ct); Lose("fencing");
        }
        public async Task RequireFencedAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) {
            Check(e); var state = (await table.GetEntityAsync<TableEntity>(Partition, "state", cancellationToken: ct)).Value;
            if (state.GetBoolean("Blocked") != true || state.GetString("Owner") != Scope(e)) throw new ManagementAccessDeniedException();
        }
        public async Task PurgeOrReconcileAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) {
            Check(e); PurgeCalls++; if (BeforePurge is { } before) await before(); if (Pending) throw new ManagementTenantPurgePendingException();
            await e.LeaseFence!.RequireAsync(ct); var receipt = await table.GetEntityIfExistsAsync<TableEntity>(Partition, "receipt", cancellationToken: ct);
            if (receipt.HasValue) { if (receipt.Value.GetString("Scope") != Scope(e)) throw new ManagementAccessDeniedException(); return; }
            var data = await table.GetEntityIfExistsAsync<TableEntity>(Partition, "data", cancellationToken: ct);
            var actions = new List<TableTransactionAction>();
            if (data.HasValue && CoverageMode != "partial") actions.Add(new(TableTransactionActionType.Delete, data.Value, data.Value.ETag));
            actions.Add(new(TableTransactionActionType.Add, new TableEntity(Partition, "receipt") { ["Scope"] = Scope(e), ["Commits"] = 1 }));
            await e.LeaseFence.RequireAsync(ct); await table.SubmitTransactionAsync(actions, ct); Lose("purging");
        }
        public async Task<IReadOnlyList<ManagementTenantPurgeCategory>> VerifyAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) {
            Check(e); await e.LeaseFence!.RequireAsync(ct); Lose("verifying");
            var present = await DataExists(); var coverage = Definition.Categories.Select(x => x with { Disposition = x.Disposition switch {
                "delete" => present ? CoverageMode == "partial" ? "retained" : "pending" : "deleted", "retain" => "retained", _ => "excluded" } }).ToArray();
            if (CoverageMode == "missing") return coverage[..2];
            if (CoverageMode == "pending") coverage[0] = coverage[0] with { Disposition = "pending" };
            return coverage;
        }
        public async Task CustomerAccessOrWrite() { if ((await table.GetEntityAsync<TableEntity>(Partition, "state")).Value.GetBoolean("Blocked") == true) throw new ManagementAccessDeniedException(); }
        public async Task ChangeRevision() { var row = (await table.GetEntityAsync<TableEntity>(Partition, "state")).Value; row["Revision"] = "v2"; await table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace); }
        public async Task<bool> DataExists() => (await table.GetEntityIfExistsAsync<TableEntity>(Partition, "data")).HasValue;
        public async Task<bool> OtherDataExists() => (await table.GetEntityIfExistsAsync<TableEntity>("unrelated-tenant", "data")).HasValue;
        public async Task<bool> GlobalDataExists() => (await table.GetEntityIfExistsAsync<TableEntity>("global-account", "credential")).HasValue;
        public async Task<int> Commits() => (await table.GetEntityAsync<TableEntity>(Partition, "receipt")).Value.GetInt32("Commits")!.Value;
        private void Lose(string phase) { if (LoseReply == phase) { LoseReply = null; throw new IOException("PRIVATE-NATIVE-ERROR"); } }
        private void Check(ManagementTenantPurgeExecution e) { if (e.Authority.TenantId != expected.Authority.TenantId || e.Authority.RealmId != expected.Authority.RealmId || e.ApplicationKey != expected.ApplicationKey || e.Environment != expected.Environment) throw new ManagementAccessDeniedException(); }
        private static string Scope(ManagementTenantPurgeExecution e) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { e.ApplicationKey, e.Environment,
            e.Authority.OrganizationId, e.Authority.ApplicationId, e.Authority.RealmId, e.Authority.TenantId, e.Authority.ActorUserId, e.JobId })));
    }
    internal sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
