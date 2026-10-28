using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Target.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class DurableExportTests
{
    [TestMethod]
    public async Task DuplicateSubmissionCreatesOneDurableIntentAndChangedInputConflicts()
    {
        var f = new Fixture();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Submit()));
        Assert.AreEqual(1, f.Store.Count); Assert.AreEqual(0, f.Exports);
        await f.Worker.RunAsync(f.Execution);
        Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
        f.FreshVersionAllowed = false;
        Assert.AreEqual("succeeded", (await f.Submit()).Job!.State);
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Jobs.SubmitAsync(f.Execution with { Authority = f.Execution.Authority with { ResourceVersion = "changed-version" } }, new("enterprise", "changed-version")));
        Assert.AreEqual(1, f.Exports); Assert.AreEqual(1, f.Delivered.Count);
    }
    [TestMethod]
    public async Task BusinessPayloadReachesOnlyTheCurrentlyAuthorizedCustomerAndNeverSupportMetadataOrAudit()
    {
        var f = new Fixture(); await f.Submit(); await f.Worker.RunAsync(f.Execution);
        var support = await f.Jobs.FindAsync(f.Execution);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(support));
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(f.Audits));
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(await f.Store.GetAsync(f.Execution)));
        var artifact = await f.Customers.RequireAsync(f.Execution, f.Customer());
        Assert.AreEqual("PRIVATE-REMOVED-MEMBER-DATA", Encoding.UTF8.GetString(f.Artifacts[artifact.Reference].Data));
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(artifact));
        f.CustomerEnabled = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Customers.RequireAsync(f.Execution, f.Customer()));
        f.CustomerEnabled = true;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Customers.RequireAsync(f.Execution, f.Customer(Guid.NewGuid())));
        var supportPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human), new("sub", f.Execution.Authority.ActorUserId.ToString())], "management"));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Customers.RequireAsync(f.Execution, supportPrincipal));
    }
    [TestMethod]
    [DataRow("output-save")] [DataRow("delivery-reply")]
    public async Task LostProviderOrStoreRepliesPauseAndExplicitReconciliationReusesOneOutputAndDelivery(string phase)
    {
        var f = new Fixture(); await f.Submit();
        f.Store.LoseArtifactSaveReply = phase == "output-save"; f.LoseDeliveryReply = phase == "delivery-reply";
        await f.Worker.RunAsync(f.Execution);
        var paused = (await f.Store.GetAsync(f.Execution))!;
        Assert.IsTrue(paused.NeedsReconciliation); Assert.AreEqual("running", paused.State);
        var calls = f.Exports; var deliveries = f.DeliveryCalls;
        await f.Worker.RunAsync(f.Execution);
        Assert.AreEqual(calls, f.Exports); Assert.AreEqual(deliveries, f.DeliveryCalls);
        f.Rebuild(); // Same durable journal, key ring and app-owned receipts after a worker restart.
        await f.Worker.RunAsync(f.Execution, reconcileUncertain: true);
        Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
        Assert.AreEqual(1, f.Artifacts.Count); Assert.AreEqual(1, f.Delivered.Count);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(await f.Jobs.FindAsync(f.Execution)) + JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    public async Task UncertainExportDoesNotAutomaticallyCallTheHookAgainAndResumesByStableJob()
    {
        var f = new Fixture(); await f.Submit(); f.LoseExportReply = true;
        await f.Worker.RunAsync(f.Execution);
        Assert.IsTrue((await f.Store.GetAsync(f.Execution))!.NeedsReconciliation);
        await f.Worker.RunAsync(f.Execution); Assert.AreEqual(1, f.Exports);
        await f.Worker.RunAsync(f.Execution, reconcileUncertain: true);
        Assert.AreEqual(2, f.Exports); Assert.AreEqual(1, f.Artifacts.Count);
        Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
    }
    [TestMethod]
    public async Task PositivePendingSignalResumesTheSameConsumerJobWithoutReportingSuccess()
    {
        var f = new Fixture(); await f.Submit(); f.Pending = true;
        await f.Worker.RunAsync(f.Execution);
        var running = (await f.Store.GetAsync(f.Execution))!;
        Assert.IsFalse(running.NeedsReconciliation); Assert.AreEqual("running", running.State);
        Assert.AreEqual(0, f.Delivered.Count);
        f.Pending = false; await f.Worker.RunAsync(f.Execution);
        Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
    }
    [TestMethod]
    public async Task ConcurrentWorkersRespectTheLeaseAndExpiredWorkersCannotPublishAnOutput()
    {
        var f = new Fixture(); await f.Submit();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeExport = async _ => { entered.TrySetResult(); await release.Task; };
        var old = f.Worker.RunAsync(f.Execution); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Worker.RunAsync(f.Execution); Assert.AreEqual(1, f.Exports);
        f.Clock.Now = f.Clock.Now.AddSeconds(31); f.BeforeExport = null;
        await f.Worker.RunAsync(f.Execution); release.SetResult(); await old;
        Assert.AreEqual(1, f.Artifacts.Count); Assert.AreEqual(1, f.Delivered.Count);
        Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
    }
    [TestMethod]
    [DataRow("partial")] [DataRow("missing")]
    public async Task CoverageIsCompleteOrExplicitlyPartialAndNeverSilentlyOmitsInventory(string mode)
    {
        var f = new Fixture(); await f.Submit(); f.CoverageMode = mode;
        await f.Worker.RunAsync(f.Execution);
        var status = (await f.Jobs.FindAsync(f.Execution)).Job!;
        Assert.AreEqual(mode == "partial" ? "partially-completed" : "failed", status.State);
        Assert.AreEqual(mode == "partial" ? 1 : 0, f.Delivered.Count);
        if (mode == "missing") { await f.Worker.CleanupAsync(f.Execution); Assert.AreEqual(0, f.Artifacts.Count); }
    }
    [TestMethod]
    public async Task ExpirationAndCancellationDenyRetrievalAndCleanupOnlyTheSelectedJob()
    {
        var f = new Fixture(); await f.Submit(); await f.Worker.RunAsync(f.Execution);
        var otherId = Guid.NewGuid(); var other = f.Execution with { JobId = otherId, Authority = f.Execution.Authority with { OperationId = otherId } };
        await f.Jobs.SubmitAsync(other, new("enterprise", "tenant-1")); await f.Worker.RunAsync(other);
        await f.Worker.CancelAsync(f.Execution);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Customers.RequireAsync(f.Execution, f.Customer()));
        await f.Worker.CleanupAsync(f.Execution); await f.Worker.CleanupAsync(f.Execution);
        Assert.AreEqual(1, f.Artifacts.Count); Assert.IsTrue(f.Artifacts.Values.All(x => x.Execution.JobId == otherId));
        f.Clock.Now = f.Clock.Now.AddHours(1);
        Assert.AreEqual("expired", (await f.Jobs.FindAsync(other)).Job!.State);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Customers.RequireAsync(other, f.Customer()));
        await f.Worker.CleanupAsync(other); Assert.AreEqual(0, f.Artifacts.Count);
    }
    [TestMethod]
    [DataRow("tenant")] [DataRow("actor")] [DataRow("app")] [DataRow("realm")] [DataRow("environment")]
    public async Task ForeignScopeCannotReadRunCancelOrRetrieveAJob(string change)
    {
        var f = new Fixture(); await f.Submit();
        var execution = change switch {
            "tenant" => f.Execution with { Authority = f.Execution.Authority with { TenantId = Guid.NewGuid(), ResourceId = Guid.NewGuid() } },
            "actor" => f.Execution with { Authority = f.Execution.Authority with { ActorUserId = Guid.NewGuid() } },
            "app" => f.Execution with { Authority = f.Execution.Authority with { ApplicationId = Guid.NewGuid() } },
            "realm" => f.Execution with { Authority = f.Execution.Authority with { RealmId = Guid.NewGuid() } },
            _ => f.Execution with { Environment = "Production" }
        };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Jobs.FindAsync(execution));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Worker.RunAsync(execution));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Worker.CancelAsync(execution));
        Assert.AreEqual(0, f.Exports);
    }
    [TestMethod]
    public async Task CiphertextCannotBeMovedToAnotherJobOrExpiryAndRetainedKeysSupportRotation()
    {
        var f = new Fixture(); await f.Submit(); await f.Worker.RunAsync(f.Execution);
        var row = (await f.Store.GetAsync(f.Execution))!;
        Assert.DoesNotContain("PRIVATE-", row.ProtectedArtifact);
        f.JobOptions.ArtifactProtectionKeys["artifact.v2"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); f.JobOptions.ActiveKeyId = "artifact.v2"; f.Rebuild();
        Assert.IsNotNull(await f.Customers.RequireAsync(f.Execution, f.Customer()));
        await f.Store.ReplaceAsync(row with { ExpiresUtc = row.ExpiresUtc.AddMinutes(1) }, row.Version);
        await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => f.Customers.RequireAsync(f.Execution, f.Customer()));
    }
    [TestMethod]
    public async Task NativePolicyAndLiveManagementAuthorityAreRequiredAndNeverExposeProviderErrors()
    {
        var f = new Fixture(); f.SupportEnabled = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Submit()); Assert.AreEqual(0, f.Store.Count);
        f.SupportEnabled = true; f.FreshVersionAllowed = false;
        await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Submit()); Assert.AreEqual(0, f.Store.Count);
        f.FreshVersionAllowed = true; await f.Submit(); f.StatusAllowed = false;
        await f.Worker.RunAsync(f.Execution); Assert.AreEqual(0, f.Exports);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Jobs.FindAsync(f.Execution));
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(f.Audits));
    }

    [TestMethod]
    public async Task CustomerRevocationDuringArtifactValidationIsRecheckedBeforePrivateReferenceReturns()
    {
        var f = new Fixture(); await f.Submit(); await f.Worker.RunAsync(f.Execution);
        f.AfterArtifactCheck = () => f.CustomerEnabled = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Customers.RequireAsync(f.Execution, f.Customer()));
    }
    [TestMethod]
    public async Task HistoricalJobsCanStillBeCancelledAndCleanedAfterWorkBindingsAreDisabled()
    {
        var f = new Fixture(); await f.Submit(); await f.Worker.RunAsync(f.Execution);
        f.DisableWork();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Worker.RunAsync(f.Execution));
        await f.Worker.CancelAsync(f.Execution); await f.Worker.CleanupAsync(f.Execution);
        Assert.HasCount(0, f.Artifacts);
    }
    [TestMethod]
    public async Task GuessedJobsCiphertextSubstitutionAndAmbiguousCleanupNeverExposeOrDeleteAnotherOutput()
    {
        var f = new Fixture(); await f.Submit(); await f.Worker.RunAsync(f.Execution);
        var id = Guid.NewGuid(); var other = f.Execution with { JobId = id, Authority = f.Execution.Authority with { OperationId = id } };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Customers.RequireAsync(other, f.Customer()));
        await f.Jobs.SubmitAsync(other, new("enterprise", "tenant-1")); await f.Worker.RunAsync(other);
        var first = (await f.Store.GetAsync(f.Execution))!; var second = (await f.Store.GetAsync(other))!;
        await f.Store.ReplaceAsync(second with { ProtectedArtifact = first.ProtectedArtifact }, second.Version);
        await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => f.Customers.RequireAsync(other, f.Customer()));
        await f.Worker.CancelAsync(other);
        await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => f.Worker.CleanupAsync(other));
        Assert.IsTrue((await f.Store.GetAsync(other))!.NeedsReconciliation);
        Assert.IsTrue(f.Artifacts.Values.Any(x => x.Execution.JobId == f.Execution.JobId));
    }
    [TestMethod]
    public async Task CancellationDuringAnActiveHookFencesItsPublicationAndCleanupWaitsForLeaseExpiry()
    {
        var f = new Fixture(); await f.Submit();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeExport = async _ => { entered.TrySetResult(); await release.Task; };
        var running = f.Worker.RunAsync(f.Execution); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Worker.CancelAsync(f.Execution); await f.Worker.CleanupAsync(f.Execution);
        Assert.IsFalse((await f.Store.GetAsync(f.Execution))!.Cleaned);
        release.SetResult(); await running; Assert.HasCount(0, f.Artifacts); Assert.HasCount(0, f.Delivered);
        f.Clock.Now = f.Clock.Now.AddSeconds(31); await f.Worker.CleanupAsync(f.Execution);
        Assert.IsTrue((await f.Store.GetAsync(f.Execution))!.Cleaned);
        Assert.AreEqual("cancelled", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
    }
    [TestMethod]
    public async Task UnavailableStorageIsExplicitAndProtectionConfigurationCannotSerializeSecrets()
    {
        var f = new Fixture(); await f.Submit(); f.Store.FailReads = true;
        var error = await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => f.Jobs.FindAsync(f.Execution));
        Assert.DoesNotContain("PRIVATE-", error.ToString());
        Assert.DoesNotContain(f.JobOptions.ArtifactProtectionKeys.Values.First(), JsonSerializer.Serialize(f.JobOptions) + f.JobOptions);
    }

    internal sealed class Fixture
    {
        public Clock Clock { get; } = new(); public Journal Store { get; } = new();
        public ManagementTenantExportExecution Execution { get; }
        public ManagementTenantExportJobOptions JobOptions { get; }
        public ManagementTenantExportJobService Jobs { get; private set; } = null!;
        public ManagementTenantExportWorker Worker { get; private set; } = null!;
        public ManagementTenantExportCustomerArtifacts Customers { get; private set; } = null!;
        public bool SupportEnabled = true, CustomerEnabled = true, FreshVersionAllowed = true, StatusAllowed = true;
        public bool LoseExportReply, LoseDeliveryReply, Pending; public string CoverageMode = "complete";
        public int Exports, DeliveryCalls;
        public Func<ManagementTenantExportExecution, Task>? BeforeExport;
        public Action? AfterArtifactCheck;
        public ConcurrentDictionary<string, Output> Artifacts { get; } = new();
        public ConcurrentDictionary<Guid, byte> Delivered { get; } = new();
        public ConcurrentBag<ManagementAuditEntity> Audits { get; } = [];
        private readonly Mock<IManagementTenantExportHandler> handler = new();
        private readonly Mock<IManagementTenantExportDelivery> delivery = new();
        private readonly Mock<IManagementTenantExportPolicy> policy = new();
        private readonly Mock<IManagementOperationAccessService> access = new();
        private readonly Mock<IManagementTenantExportCustomerAuthorization> customer = new();
        private readonly Mock<IManagementAuditRepository> audit = new();
        private readonly ManagementDiagnosticsOptions deployment = new() { ApplicationKey = "fixture", Environment = "Testing" };
        private readonly ManagementOperationTargetOptions target;
        public Fixture()
        {
            var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
            Execution = new(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tenant, Guid.NewGuid(), id) { ResourceId = tenant, ResourceVersion = "tenant-1" }, "fixture", "Testing", id, "enterprise", 1);
            target = new() { RealmId = Execution.Authority.RealmId, DedicatedApplicationRealm = true, EnabledOperations = [ManagementTenantDataOperations.Export],
                TrustedPortals = [new() { OrganizationId = Execution.Authority.OrganizationId, ApplicationId = Execution.Authority.ApplicationId, Issuer = "portal", Audience = "target" }] };
            JobOptions = new() { ActiveKeyId = "artifact.v1", ArtifactProtectionKeys = new() { ["artifact.v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }, LeaseSeconds = 30 };
            handler.SetupGet(x => x.ProtocolVersion).Returns(1); handler.Setup(x => x.Describe()).Returns(ExportContractTests.Definition);
            handler.Setup(x => x.ExecuteOrResumeAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<CancellationToken>())).Returns(async (ManagementTenantExportExecution e, CancellationToken ct) => {
                Interlocked.Increment(ref Exports); if (BeforeExport is { } before) await before(e); if (Pending) throw new ManagementTenantExportPendingException();
                await e.LeaseFence!.RequireAsync(ct); var reference = "PRIVATE-ARTIFACT/" + ScopeKey(e);
                var expiry = Clock.Now.AddMinutes(30) < e.ArtifactExpiresUtc ? Clock.Now.AddMinutes(30) : e.ArtifactExpiresUtc!.Value;
                var output = Artifacts.GetOrAdd(reference, _ => new(e, "PRIVATE-REMOVED-MEMBER-DATA"u8.ToArray(), expiry));
                if (LoseExportReply) { LoseExportReply = false; throw new IOException("PRIVATE-PROVIDER-ERROR"); }
                var coverage = ExportContractTests.Definition.Datasets.Select(x => new ManagementTenantExportCoverage(x.Key, x.Kind, "completed")).ToArray();
                if (CoverageMode == "partial") coverage[0] = coverage[0] with { Disposition = "retained" };
                if (CoverageMode == "missing") coverage = coverage[..2];
                return new ManagementTenantExportOutput(new(reference, output.ExpiresUtc), coverage);
            });
            handler.Setup(x => x.CancelAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<CancellationToken>())).Returns(async (ManagementTenantExportExecution e, CancellationToken ct) => {
                await e.LeaseFence!.RequireAsync(ct); foreach (var pair in Artifacts.Where(x => ScopeKey(x.Value.Execution) == ScopeKey(e)).ToArray()) Artifacts.TryRemove(pair.Key, out _);
            });
            delivery.SetupGet(x => x.ProtocolVersion).Returns(1);
            delivery.Setup(x => x.RequireArtifactAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<ManagementTenantExportArtifact>(), It.IsAny<CancellationToken>()))
                .Returns((ManagementTenantExportExecution e, ManagementTenantExportArtifact a, CancellationToken _) => {
                    if (Artifacts.TryGetValue(a.Reference, out var output) && ScopeKey(output.Execution) != ScopeKey(e)) throw new ManagementAccessDeniedException();
                    AfterArtifactCheck?.Invoke();
                    return Task.CompletedTask;
                });
            delivery.Setup(x => x.DeliverOrReconcileAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<ManagementTenantExportArtifact>(), It.IsAny<CancellationToken>()))
                .Returns(async (ManagementTenantExportExecution e, ManagementTenantExportArtifact _, CancellationToken ct) => {
                    await e.LeaseFence!.RequireAsync(ct); DeliveryCalls++; Delivered.TryAdd(e.JobId, 0);
                    if (LoseDeliveryReply) { LoseDeliveryReply = false; throw new IOException("PRIVATE-DELIVERY-ERROR"); }
                });
            delivery.Setup(x => x.RevokeAndDeleteAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<ManagementTenantExportArtifact>(), It.IsAny<CancellationToken>()))
                .Returns(async (ManagementTenantExportExecution e, ManagementTenantExportArtifact a, CancellationToken ct) => { await e.LeaseFence!.RequireAsync(ct); Artifacts.TryRemove(a.Reference, out _); });
            policy.SetupGet(x => x.ProtocolVersion).Returns(1);
            policy.Setup(x => x.RequireAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((ManagementTenantExportExecution _, string version, CancellationToken _) => FreshVersionAllowed && version == "tenant-1" ? Task.CompletedTask : Task.FromException(new ManagementOperationPreconditionException()));
            policy.Setup(x => x.RequireStatusAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<CancellationToken>())).Returns(() => StatusAllowed ? Task.CompletedTask : Task.FromException(new ManagementAccessDeniedException()));
            access.SetupGet(x => x.ProtocolVersion).Returns(1);
            access.Setup(x => x.RequireAsync(ManagementTenantDataOperations.Export, It.IsAny<ManagementOperationContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => SupportEnabled ? Task.CompletedTask : Task.FromException(new ManagementAccessDeniedException()));
            customer.SetupGet(x => x.ProtocolVersion).Returns(1);
            customer.Setup(x => x.RequireAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .Returns((Guid realm, Guid tenant, Guid _, ClaimsPrincipal principal, CancellationToken _) => CustomerEnabled && realm == Execution.Authority.RealmId && principal.HasClaim("tenant", tenant.ToString("D"))
                    ? Task.CompletedTask : Task.FromException(new ManagementAccessDeniedException()));
            audit.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>())).Callback<ManagementAuditEntity, CancellationToken>((entry, _) => Audits.Add(entry)).Returns(Task.CompletedTask);
            Rebuild();
        }
        public void Rebuild()
        {
            var executor = new ServiceOperationExecutor(); var options = Options.Create(JobOptions); var d = Options.Create(deployment); var t = Options.Create(target);
            Jobs = new(Store, access.Object, handler.Object, policy.Object, Options.Create(new ManagementTenantExportOptions { Enabled = true, PolicyKeys = ["enterprise"] }), options, d, t, Clock, executor);
            Worker = new(Store, handler.Object, delivery.Object, policy.Object, audit.Object, Options.Create(new ManagementAuditOptions()), options, Clock, executor, d, t);
            Customers = new(Store, customer.Object, delivery.Object, options, Clock, executor, d, t);
        }
        public Task<ManagementTenantExportResult> Submit() => Jobs.SubmitAsync(Execution, new("enterprise", "tenant-1"));
        public void DisableWork() { target.EnabledOperations = []; target.TrustedPortals = []; }
        private static string ScopeKey(ManagementTenantExportExecution e) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            e.ApplicationKey, e.Environment, e.JobId, e.Authority.OrganizationId, e.Authority.ApplicationId, e.Authority.RealmId, e.Authority.TenantId, e.Authority.ActorUserId })));
        public ClaimsPrincipal Customer(Guid? tenant = null) => new(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString("D")), new("tenant", (tenant ?? Execution.Authority.TenantId).ToString("D"))], "customer"));
        public sealed record Output(ManagementTenantExportExecution Execution, byte[] Data, DateTimeOffset ExpiresUtc);
    }
    internal sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    internal sealed class Journal : IManagementTenantExportJobRepository
    {
        private readonly object gate = new(); private readonly Dictionary<(Guid, Guid), ManagementTenantExportJobEntity> rows = [];
        public int Count { get { lock (gate) return rows.Count; } }
        public bool LoseArtifactSaveReply;
        public bool FailReads;
        public int ProtocolVersion => 1; public bool Durable => false;
        private static ManagementTenantExportJobEntity Copy(ManagementTenantExportJobEntity row) => JsonSerializer.Deserialize<ManagementTenantExportJobEntity>(JsonSerializer.Serialize(row))! with { Version = row.Version, ProtectedArtifact = row.ProtectedArtifact };
        public Task<ManagementTenantExportJobEntity?> GetAsync(ManagementTenantExportExecution scope, CancellationToken ct = default)
        {
            if (FailReads) throw new IOException("PRIVATE-STORAGE-OFFLINE");
            lock (gate)
            {
                if (!rows.TryGetValue((scope.Authority.TenantId, scope.JobId), out var row)) return Task.FromResult<ManagementTenantExportJobEntity?>(null);
                if (row.Execution.ApplicationKey != scope.ApplicationKey || row.Execution.Environment != scope.Environment || row.Execution.Authority.OrganizationId != scope.Authority.OrganizationId ||
                    row.Execution.Authority.ApplicationId != scope.Authority.ApplicationId || row.Execution.Authority.RealmId != scope.Authority.RealmId || row.Execution.Authority.ActorUserId != scope.Authority.ActorUserId)
                    throw new ManagementAccessDeniedException();
                return Task.FromResult<ManagementTenantExportJobEntity?>(Copy(row));
            }
        }
        public Task<bool> TryCreateAsync(ManagementTenantExportJobEntity entity, CancellationToken ct = default)
        { lock (gate) return Task.FromResult(rows.TryAdd((entity.Execution.Authority.TenantId, entity.Execution.JobId), Copy(entity with { Version = Guid.NewGuid().ToString("N") }))); }
        public Task<ManagementTenantExportJobEntity> ReplaceAsync(ManagementTenantExportJobEntity entity, string expectedVersion, CancellationToken ct = default)
        {
            lock (gate)
            {
                var key = (entity.Execution.Authority.TenantId, entity.Execution.JobId);
                if (!rows.TryGetValue(key, out var old) || old.Version != expectedVersion || expectedVersion == "*") throw new ManagementOperationConflictException();
                var saved = Copy(entity with { Version = Guid.NewGuid().ToString("N") }); rows[key] = saved;
                if (LoseArtifactSaveReply && old.Phase == "export" && saved.Phase == "delivery") { LoseArtifactSaveReply = false; throw new IOException("PRIVATE-STORAGE-ERROR"); }
                return Task.FromResult(Copy(saved));
            }
        }
        public Task<ManagementTenantExportJobPage> ListPageAsync(ManagementTenantExportDeployment deployment, int pageSize, string? continuationToken, CancellationToken ct = default)
        { lock (gate) return Task.FromResult(new ManagementTenantExportJobPage(rows.Values.Select(x => x.Execution).Take(pageSize).ToArray(), null)); }
    }
}
