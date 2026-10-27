using System.Text.Json;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Licensing;
using IBeam.Management.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.Licensing;

[TestClass]
public sealed class LicenseStatusTests
{
    [TestMethod]
    [DataRow("active")] [DataRow("trialing")] [DataRow("grace")] [DataRow("manual")]
    public async Task ExplicitSuspendRestorePreservesNativePlanSeatsAndUnrelatedLicences(string original)
    {
        var f = await Fixture.Create(original);
        var before = await f.Store.GetLicenseAsync(f.Context.TenantId, f.Context.ResourceId);
        var other = await f.Native.GrantLicenseAsync(Guid.NewGuid(), new() { PlanKey = "other", StartsUtc = f.Clock.Now.AddDays(-1) });
        var suspended = await f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, f.Context.ResourceVersion!);
        Assert.AreEqual("succeeded", suspended.Outcome); Assert.AreEqual("suspended", suspended.Status);
        Assert.IsFalse((await f.Store.GetLicenseAsync(f.Context.TenantId, f.Context.ResourceId))!.IsActive(f.Clock.Now));
        var restore = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = suspended.Version };
        var restored = await f.Target.ChangeAsync(ManagementLicenseOperations.Restore, restore, restore.ResourceId, restore.ResourceVersion!);
        Assert.AreEqual(original, restored.Status); Assert.AreNotEqual(suspended.Version, restored.Version);
        var current = (await f.Store.GetLicenseAsync(restore.TenantId, restore.ResourceId))!;
        Assert.AreEqual(before!.PlanKey, current.PlanKey); Assert.AreEqual(before.SeatLimit, current.SeatLimit);
        CollectionAssert.AreEqual(before.Entitlements.ToArray(), current.Entitlements.ToArray());
        Assert.AreEqual(JsonSerializer.Serialize(before.Metadata), JsonSerializer.Serialize(current.Metadata));
        Assert.AreEqual(JsonSerializer.Serialize(before.Limits), JsonSerializer.Serialize(current.Limits));
        Assert.AreEqual(before.ExpiresUtc, current.ExpiresUtc); Assert.AreEqual(before.GraceEndsUtc, current.GraceEndsUtc);
        Assert.AreEqual("active", (await f.Store.GetLicenseAsync(other.TenantId, other.LicenseId))!.Status);
    }

    [TestMethod]
    public async Task ReadAndReceiptLookupDoNotInitializeVersionsOrMutateNativeState()
    {
        var f = await Fixture.Create(); var version = f.Context.ResourceVersion;
        var read = await f.Target.ReadAsync(f.Context);
        var unknown = await f.Target.FindAsync(ManagementLicenseOperations.Suspend, f.Context);
        Assert.AreEqual("unknown", unknown.Outcome); Assert.IsNull(unknown.Status); Assert.IsNull(unknown.Version);
        Assert.AreEqual(version, read.Version);
        Assert.AreEqual(version, (await f.Native.ReadStatusAsync(f.Context.TenantId, f.Context.ResourceId)).Version);
        // A read did not install the write fence: a legacy unversioned native writer is still usable.
        var row = (await f.Store.GetLicenseAsync(f.Context.TenantId, f.Context.ResourceId))!;
        await f.Store.UpsertLicenseAsync(row with { Version = "", DisplayName = "native update" });
    }

    [TestMethod]
    [DataRow("tenants-status-only")] [DataRow("missing-read")] [DataRow("missing-machine")]
    [DataRow("revoked-human")] [DataRow("app")] [DataRow("realm")] [DataRow("tenant")] [DataRow("user")]
    public async Task IndependentLiveAuthorityDeniesBeforeNativeWrites(string change)
    {
        var f = await Fixture.Create(); var context = f.Context;
        switch (change)
        {
            case "tenants-status-only": f.Access.Human = [ManagementCustomerIdentityOperations.TenantsStatus]; break;
            case "missing-read": f.Access.Human = [ManagementLicenseOperations.Suspend]; break;
            case "missing-machine": f.Access.Machine = [ManagementLicenseOperations.Read]; break;
            case "revoked-human": f.Access.Revoked = true; break;
            case "app": context = context with { ApplicationId = Guid.NewGuid() }; break;
            case "realm": context = context with { RealmId = Guid.NewGuid() }; break;
            case "tenant": context = context with { TenantId = Guid.NewGuid() }; break;
            case "user": context = context with { ActorUserId = Guid.NewGuid() }; break;
        }
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, context, context.ResourceId, context.ResourceVersion!));
        Assert.AreEqual(f.Context.ResourceVersion, (await f.Native.ReadStatusAsync(f.Context.TenantId, f.Context.ResourceId)).Version);
    }

    [TestMethod]
    [DataRow("expired")] [DataRow("revoked")] [DataRow("policy")] [DataRow("not-started")]
    public async Task RestoreNeverResurrectsInvalidEntitlementsOrBypassesHostPolicy(string change)
    {
        var f = await Fixture.Create();
        var suspended = await f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, f.Context.ResourceVersion!);
        if (change == "expired") f.Clock.Now = f.Clock.Now.AddDays(2);
        if (change == "policy") f.Policy.Allow = false;
        if (change == "revoked") await f.Native.RevokeLicenseAsync(f.Context.TenantId, f.Context.ResourceId, "synthetic");
        if (change == "not-started") await f.Native.UpdateLicenseAsync(f.Context.TenantId, f.Context.ResourceId, new() { StartsUtc = f.Clock.Now.AddMinutes(30) });
        var current = await f.Native.ReadStatusAsync(f.Context.TenantId, f.Context.ResourceId);
        var restore = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = current.Version };
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Target.ChangeAsync(ManagementLicenseOperations.Restore, restore, restore.ResourceId, current.Version));
        Assert.AreEqual(current.Version, (await f.Native.ReadStatusAsync(restore.TenantId, restore.ResourceId)).Version);
    }

    [TestMethod]
    public async Task SameIntentReplayNeverReappliesOldStatusAndChangedIntentConflicts()
    {
        var f = await Fixture.Create();
        var suspend = await f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, f.Context.ResourceVersion!);
        var restore = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = suspend.Version };
        var restored = await f.Target.ChangeAsync(ManagementLicenseOperations.Restore, restore, restore.ResourceId, restore.ResourceVersion!);
        var replay = await f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, f.Context.ResourceVersion!);
        Assert.AreEqual(suspend, replay);
        Assert.AreEqual(restored.Version, (await f.Native.ReadStatusAsync(restore.TenantId, restore.ResourceId)).Version);
        Assert.AreEqual("active", (await f.Native.ReadStatusAsync(restore.TenantId, restore.ResourceId)).License.Status);
        var altered = f.Context with { ResourceVersion = restored.Version };
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, altered, altered.ResourceId, altered.ResourceVersion!));
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Target.FindAsync(ManagementLicenseOperations.Restore, f.Context));
    }

    [TestMethod]
    public async Task StaleNativeWriterAndConcurrentTransitionsCannotOverwriteCommittedStatus()
    {
        var f = await Fixture.Create();
        var stale = (await f.Store.GetLicenseAsync(f.Context.TenantId, f.Context.ResourceId))!;
        var first = await f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, f.Context.ResourceVersion!);
        await Assert.ThrowsAsync<LicenseStatusPreconditionException>(() => f.Store.UpsertLicenseAsync(stale with { Status = "active" }));
        await Assert.ThrowsAsync<LicenseStatusPreconditionException>(() => f.Store.UpsertLicenseAsync(stale with { Version = "" }));
        await Assert.ThrowsAsync<LicenseStatusConflictException>(() => f.Store.DeleteLicenseAsync(stale.TenantId, stale.LicenseId));
        var competing = f.Context with { OperationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, competing, competing.ResourceId, competing.ResourceVersion!));
        Assert.AreEqual(first.Version, (await f.Native.ReadStatusAsync(stale.TenantId, stale.LicenseId)).Version);
    }

    [TestMethod]
    public async Task LostReplyProducesUnknownAndReadOnlyLookupRecoversWithoutAnotherMutation()
    {
        var f = await Fixture.Create(); f.Store.LoseReply = true;
        var unknown = await f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, f.Context.ResourceVersion!);
        Assert.AreEqual("unknown", unknown.Outcome); Assert.IsNull(unknown.Status); Assert.IsNull(unknown.Version);
        Assert.AreEqual(1, f.Store.Commits);
        var known = await f.Target.FindAsync(ManagementLicenseOperations.Suspend, f.Context);
        Assert.AreEqual("succeeded", known.Outcome); Assert.AreEqual("suspended", known.Status); Assert.AreEqual(1, f.Store.Commits);
        Assert.DoesNotContain("PROVIDER-PRIVATE-CANARY", JsonSerializer.Serialize(new[] { unknown, known }));
        f.Access.Revoked = true;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Target.FindAsync(ManagementLicenseOperations.Suspend, f.Context));
        Assert.AreEqual(1, f.Store.Commits);
    }

    [TestMethod]
    public async Task WrongResourceWildcardVersionAndChangedSignedVersionAreRejected()
    {
        var f = await Fixture.Create();
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, Guid.NewGuid(), f.Context.ResourceVersion!));
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, "*"));
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, "changed-header"));
        Assert.AreEqual(0, f.Store.Commits);
    }

    [TestMethod]
    public async Task SimultaneousIndependentIntentsShareOneAtomicVersionFence()
    {
        var f = await Fixture.Create(); f.Store.Barrier = new(8);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var context = f.Context with { OperationId = Guid.NewGuid() };
            try { await f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, context, context.ResourceId, context.ResourceVersion!); return "committed"; }
            catch (ManagementOperationPreconditionException) { return "stale"; }
        }));
        Assert.HasCount(1, results.Where(x => x == "committed")); Assert.HasCount(7, results.Where(x => x == "stale"));
    }

    [TestMethod]
    public async Task SimultaneousSameIntentReturnsOneImmutableReceipt()
    {
        var f = await Fixture.Create(); f.Store.Barrier = new(8);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            f.Target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, f.Context.ResourceVersion!)));
        Assert.HasCount(1, results.Select(x => x.Version).Distinct());
        Assert.AreEqual(results[0].Version, (await f.Native.ReadStatusAsync(f.Context.TenantId, f.Context.ResourceId)).Version);
    }

    internal sealed class Fixture
    {
        public FaultStore Store { get; } = new();
        public Clock Clock { get; } = new();
        public RestorePolicy Policy { get; } = new();
        public LiveAccess Access { get; } = new();
        public TenantLicenseService Native { get; private set; } = null!;
        public ManagementLicenseTargetService Target { get; private set; } = null!;
        public ManagementOperationContext Context { get; private set; } = null!;
        public static async Task<Fixture> Create(string status = "active", IServiceOperationExecutor? executor = null)
        {
            var f = new Fixture();
            var catalog = new Mock<ILicensePlanCatalogProvider>();
            catalog.Setup(x => x.GetPlanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((LicensePlanInfo?)null);
            var nativeStatus = new TenantLicenseStatusAdministrationService(f.Store, f.Policy, new ServiceOperationExecutor(), f.Clock);
            f.Native = new(f.Store, catalog.Object, new ServiceOperationExecutor(), nativeStatus);
            var tenant = Guid.NewGuid();
            var license = await f.Native.GrantLicenseAsync(tenant, new() { PlanKey = "synthetic", Status = status,
                StartsUtc = f.Clock.Now.AddDays(-1), ExpiresUtc = f.Clock.Now.AddHours(1), GraceEndsUtc = f.Clock.Now.AddHours(2),
                Entitlements = ["synthetic.access"], SeatLimit = 10, Limits = new() { ["items"] = 12 }, Metadata = new() { ["safe"] = "fixture" } });
            var snapshot = await f.Native.ReadStatusAsync(tenant, license.LicenseId);
            f.Context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tenant, Guid.NewGuid(), Guid.NewGuid())
                { ResourceId = license.LicenseId, ResourceVersion = snapshot.Version };
            f.Access.Expected = f.Context;
            f.Target = new(f.Native, f.Access, executor ?? new ServiceOperationExecutor());
            return f;
        }
    }
    internal sealed class Clock : TimeProvider
    { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    internal sealed class RestorePolicy : ILicenseStatusRestorePolicy
    {
        public bool Allow { get; set; } = true;
        public Task RequireAsync(LicenseStatusCommand command, TenantLicenseRecord current, TenantLicenseRecord candidate, CancellationToken ct = default)
        { if (!Allow) throw new LicenseStatusConflictException(); return Task.CompletedTask; }
    }
    internal sealed class LiveAccess : IManagementOperationAccessService
    {
        public int ProtocolVersion => 1;
        public ManagementOperationContext Expected { get; set; } = null!;
        public bool Revoked { get; set; }
        public string[] Human { get; set; } = [ManagementLicenseOperations.Read, ManagementLicenseOperations.Suspend, ManagementLicenseOperations.Restore];
        public string[] Machine { get; set; } = [ManagementLicenseOperations.Read, ManagementLicenseOperations.Suspend, ManagementLicenseOperations.Restore];
        private readonly ManagementOperationRegistry registry = new(ManagementLicensingServiceExtensions.LicenseStatusDescriptors);
        public Task RequireAsync(string operation, ManagementOperationContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Revoked || context.OrganizationId != Expected.OrganizationId || context.ApplicationId != Expected.ApplicationId || context.RealmId != Expected.RealmId ||
                context.TenantId != Expected.TenantId || context.ActorUserId != Expected.ActorUserId || context.ResourceId != Expected.ResourceId ||
                registry.RequiredFor(operation).Any(x => !Human.Contains(x) || !Machine.Contains(x))) throw new ManagementAccessDeniedException();
            return Task.CompletedTask;
        }
    }
    internal sealed class FaultStore : ILicensingStore, ILicenseStatusConditionalStore, ILicenseSeatConditionalStore
    {
        private readonly InMemoryLicensingStore inner = new();
        public bool LoseReply { get; set; }
        private int commits;
        public int Commits => Volatile.Read(ref commits);
        public AsyncBarrier? Barrier { get; set; }
        public int StatusProtocolVersion => 1;
        public bool NativeWritersParticipate => true;
        public bool DurableStatusReceipts => false;
        public int SeatProtocolVersion => 1;
        public bool NativeSeatWritersParticipate => true;
        public bool DurableSeatReceipts => false;
        public Task<bool> RequiresSeatProtectionAsync(Guid tenant, Guid license, CancellationToken ct = default) => inner.RequiresSeatProtectionAsync(tenant, license, ct);
        public Task<LicenseSeatSnapshot?> ReadSeatsAsync(Guid tenant, Guid license, int maximum, CancellationToken ct = default) => inner.ReadSeatsAsync(tenant, license, maximum, ct);
        public Task<LicenseSeatReceipt?> FindSeatReceiptAsync(Guid tenant, Guid license, Guid id, string scope, string transition, CancellationToken ct = default)
            => inner.FindSeatReceiptAsync(tenant, license, id, scope, transition, ct);
        public Task ApplyNativeSeatAsync(Guid tenant, Guid license, string version, LicenseSeatAssignmentInfo? assignment, Guid? remove, CancellationToken ct = default)
            => inner.ApplyNativeSeatAsync(tenant, license, version, assignment, remove, ct);
        public async Task<LicenseSeatReceipt> CommitSeatAsync(LicenseSeatCommit commit, CancellationToken ct = default)
        {
            Interlocked.Increment(ref commits); if (Barrier is not null) await Barrier.Arrive(ct);
            var value = await inner.CommitSeatAsync(commit, ct);
            if (LoseReply) { LoseReply = false; throw new IOException("PROVIDER-PRIVATE-CANARY"); }
            return value;
        }
        public Task<IReadOnlyList<TenantLicenseRecord>> ListLicensesAsync(Guid tenant, CancellationToken ct = default) => inner.ListLicensesAsync(tenant, ct);
        public Task<TenantLicenseRecord?> GetLicenseAsync(Guid tenant, Guid license, CancellationToken ct = default) => inner.GetLicenseAsync(tenant, license, ct);
        public Task<TenantLicenseRecord> UpsertLicenseAsync(TenantLicenseRecord row, CancellationToken ct = default) => inner.UpsertLicenseAsync(row, ct);
        public Task DeleteLicenseAsync(Guid tenant, Guid license, CancellationToken ct = default) => inner.DeleteLicenseAsync(tenant, license, ct);
        public Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenant, Guid license, CancellationToken ct = default) => inner.ListAssignmentsAsync(tenant, license, ct);
        public Task<LicenseSeatAssignmentInfo> AddAssignmentAsync(LicenseSeatAssignmentInfo assignment, CancellationToken ct = default) => inner.AddAssignmentAsync(assignment, ct);
        public Task DeleteAssignmentAsync(Guid tenant, Guid license, Guid assignment, CancellationToken ct = default) => inner.DeleteAssignmentAsync(tenant, license, assignment, ct);
        public Task<LicenseStatusSnapshot?> ReadStatusAsync(Guid tenant, Guid license, CancellationToken ct = default) => inner.ReadStatusAsync(tenant, license, ct);
        public Task<LicenseStatusReceipt?> FindStatusReceiptAsync(Guid tenant, Guid license, Guid id, string scope, string transition, CancellationToken ct = default)
            => inner.FindStatusReceiptAsync(tenant, license, id, scope, transition, ct);
        public async Task<LicenseStatusReceipt> CommitStatusAsync(LicenseStatusCommit commit, CancellationToken ct = default)
        {
            Interlocked.Increment(ref commits);
            if (Barrier is not null) await Barrier.Arrive(ct);
            var value = await inner.CommitStatusAsync(commit, ct);
            if (LoseReply) { LoseReply = false; throw new IOException("PROVIDER-PRIVATE-CANARY"); }
            return value;
        }
    }
    internal sealed class AsyncBarrier(int count)
    {
        private int arrived;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Arrive(CancellationToken ct)
        { if (Interlocked.Increment(ref arrived) >= count) ready.TrySetResult(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); }
    }
}
