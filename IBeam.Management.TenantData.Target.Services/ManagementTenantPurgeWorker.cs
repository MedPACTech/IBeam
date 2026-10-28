using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.TenantData.Target.Services;

public sealed class ManagementTenantPurgeWorker(IManagementTenantPurgeJournalRepository repository, IManagementTenantPurgeHandler handler,
    IManagementTenantPurgePolicy policy, IEnumerable<IManagementTenantPurgeAccessFence> fences, IManagementAuditRepository audit,
    IOptions<ManagementAuditOptions> auditOptions, IOptions<ManagementTenantPurgeJobOptions> options, TimeProvider clock,
    IServiceOperationExecutor operations, IOptions<ManagementDiagnosticsOptions> deployment, IOptions<ManagementOperationTargetOptions> target) : IManagementTenantPurgeWorker
{
    public Task RunAsync(ManagementTenantPurgeExecution execution, bool reconcileUncertain = false, CancellationToken ct = default)
        => Run(execution, async token => {
            var row = await Load(execution, token);
            if (row.Phase == "done" || row.State == "cancelled" || row.NeedsReconciliation && !reconcileUncertain) return;
            var nativeFences = fences.ToArray();
            if (!row.FenceKeys.SequenceEqual(ManagementTenantPurgeJobService.FenceKeys(nativeFences)) ||
                row.Execution.InventoryFingerprint != ManagementTenantPurgeProtocol.Fingerprint(handler.Describe())) throw new ManagementOperationUnavailableException();
            await policy.RequireStatusAsync(row.Execution, token);
            var now = clock.GetUtcNow();
            if (row.LeaseId is not null && row.LeaseExpiresUtc > now) return;
            try { row = await repository.ReplaceJobAsync(row with { LeaseId = Guid.NewGuid(), LeaseExpiresUtc = now.AddSeconds(options.Value.LeaseSeconds), UpdatedUtc = now }, row.Version, token); }
            catch (ManagementOperationConflictException) { return; }
            var lease = new Fence(repository, row.Execution, row.LeaseId!.Value, row.LeaseExpiresUtc!.Value, clock, nativeFences,
                () => ManagementTenantPurgeJobService.Guard(execution, deployment.Value, target.Value), t => policy.RequireStatusAsync(row.Execution, t));
            var native = row.Execution with { LeaseFence = lease };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(row.LeaseExpiresUtc!.Value - clock.GetUtcNow());
            try {
                if (reconcileUncertain && row.State == "failed" && row.FailureCode == "verification-failed") {
                    // Operator-authorized repair reconciles the same native job; it does not reset access, identity or receipts.
                    row = await Save(row with { Phase = "purging", State = "purging", NeedsReconciliation = true }, row, lease, deadline.Token);
                }
                if (row.Phase == "fencing") {
                    // Persist uncertainty BEFORE an external commit; a crash here cannot cause unattended duplicate work.
                    row = await Save(row with { WorkStarted = true, NeedsReconciliation = true, State = "fencing", FailureCode = null }, row, lease, deadline.Token);
                    await Record(row, false, deadline.Token);
                    foreach (var nativeFence in nativeFences) {
                        await lease.JournalAsync(deadline.Token);
                        await policy.RequireStatusAsync(row.Execution, deadline.Token);
                        await nativeFence.FenceOrReconcileAsync(native, row.Plan.ExpectedVersion, deadline.Token);
                        await lease.JournalAsync(deadline.Token);
                    }
                    lease.NativeReady = true;
                    await lease.RequireAsync(deadline.Token);
                    row = await Save(row with { Phase = "purging", State = "purging", NeedsReconciliation = false }, row, lease, deadline.Token);
                }
                lease.NativeReady = true;
                if (row.Phase == "purging") {
                    await lease.RequireAsync(deadline.Token);
                    row = await Save(row with { NeedsReconciliation = true, State = "purging", FailureCode = null }, row, lease, deadline.Token);
                    await Record(row, false, deadline.Token);
                    await handler.PurgeOrReconcileAsync(native, deadline.Token);
                    await lease.RequireAsync(deadline.Token);
                    row = await Save(row with { Phase = "verifying", State = "verifying", NeedsReconciliation = false }, row, lease, deadline.Token);
                }
                if (row.Phase == "verifying") {
                    await lease.RequireAsync(deadline.Token);
                    row = await Save(row with { NeedsReconciliation = true, State = "verifying", FailureCode = null }, row, lease, deadline.Token);
                    var coverage = await handler.VerifyAsync(native, deadline.Token);
                    await lease.RequireAsync(deadline.Token);
                    var partial = coverage.Any(x => x.Disposition == "retained" && row.Definition.Categories.Any(d => d.Key == x.Key && d.Disposition == "delete"));
                    var proposed = row with { Coverage = coverage.ToArray(), State = partial ? "partially-completed" : "succeeded", FailureCode = null };
                    try { _ = ManagementTenantPurgeJobService.Project(proposed, "found", clock.GetUtcNow()); }
                    catch { throw new ManagementTenantPurgePermanentFailureException("verification-failed"); }
                    row = await Save(proposed with { Phase = "done", NeedsReconciliation = false }, row, lease, deadline.Token, release: true);
                    await Record(row, !partial, deadline.Token);
                }
            } catch (ManagementAuditUnavailableException) { throw; }
            catch (Exception ex) {
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try {
                    var latest = await Load(execution, recovery.Token);
                    if (latest.LeaseId != row.LeaseId || latest.LeaseExpiresUtc <= clock.GetUtcNow() || latest.State == "cancelled") return;
                    var pending = ex is ManagementTenantPurgePendingException;
                    var permanent = ex as ManagementTenantPurgePermanentFailureException;
                    var next = latest with { State = pending ? latest.Phase : permanent is null ? "unknown" : "failed",
                        NeedsReconciliation = !pending, FailureCode = pending ? null : permanent?.Code ?? "unknown",
                        UpdatedUtc = clock.GetUtcNow(), LeaseId = null, LeaseExpiresUtc = null };
                    row = await repository.ReplaceJobAsync(next, latest.Version, recovery.Token);
                    await Record(row, false, recovery.Token);
                } catch (ManagementOperationConflictException) { }
            }
        }, ct);
    public Task CancelBeforeFenceAsync(ManagementTenantPurgeExecution execution, CancellationToken ct = default)
        => Run(execution, async token => {
            var row = await Load(execution, token);
            if (row.State == "cancelled") return;
            if (row.WorkStarted || row.Phase != "fencing" || row.NeedsReconciliation) throw new ManagementOperationConflictException();
            row = await repository.ReplaceJobAsync(row with { State = "cancelled", Phase = "done", FailureCode = "cancelled", LeaseId = null, LeaseExpiresUtc = null, UpdatedUtc = clock.GetUtcNow() }, row.Version, token);
            await Record(row, false, token);
        }, ct);
    private async Task<ManagementTenantPurgeJournalEntity> Load(ManagementTenantPurgeExecution execution, CancellationToken ct)
    {
        ManagementTenantPurgeJobService.Guard(execution, deployment.Value, target.Value);
        return await repository.GetJobAsync(execution, ct) ?? throw new ManagementAccessDeniedException();
    }
    private async Task<ManagementTenantPurgeJournalEntity> Save(ManagementTenantPurgeJournalEntity next, ManagementTenantPurgeJournalEntity previous, Fence lease, CancellationToken ct, bool release = false)
    {
        await lease.JournalAsync(ct);
        return await repository.ReplaceJobAsync(next with { UpdatedUtc = clock.GetUtcNow(), LeaseId = release ? null : previous.LeaseId,
            LeaseExpiresUtc = release ? null : previous.LeaseExpiresUtc }, previous.Version, ct);
    }
    private async Task Record(ManagementTenantPurgeJournalEntity row, bool succeeded, CancellationToken ct)
    {
        try {
            var now = clock.GetUtcNow();
            await audit.AppendAsync(new ManagementAuditEntity { AuditId = Guid.NewGuid(), OccurredUtc = now, ExpiresUtc = now.AddDays(auditOptions.Value.RetentionDays),
                OrganizationId = row.Execution.Authority.OrganizationId, ApplicationId = row.Execution.Authority.ApplicationId,
                SubjectId = row.Execution.JobId, SubjectType = "system", ApplicationKey = row.Execution.ApplicationKey, Environment = row.Execution.Environment,
                Operation = "management.tenants.delete.worker", Succeeded = succeeded, CorrelationId = row.Execution.JobId.ToString("N") }, ct);
        } catch { throw new ManagementAuditUnavailableException(); }
    }
    private Task Run(ManagementTenantPurgeExecution execution, Func<CancellationToken, Task> action, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            try { await action(token); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationConflictException or ManagementOperationUnavailableException or ManagementAuditUnavailableException) &&
                !(ex is OperationCanceledException && token.IsCancellationRequested)) { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.tenants.delete.worker", AuditAction = "management.tenants.delete.worker",
            // Explicit system audit entries above distinguish verified completion from partial/unknown work.
            EntityId = execution.Authority.TenantId, EntityName = "ManagementTenantPurgeJournal", PermissionEnabled = false, AuditEnabled = false }, ct);
    private sealed class Fence(IManagementTenantPurgeJournalRepository repository, ManagementTenantPurgeExecution execution, Guid id,
        DateTimeOffset expires, TimeProvider clock, IManagementTenantPurgeAccessFence[] nativeFences, Action guard, Func<CancellationToken, Task> policy) : IManagementTenantPurgeLeaseFence
    {
        public bool NativeReady { get; set; }
        public DateTimeOffset ExpiresUtc => expires;
        public async Task JournalAsync(CancellationToken ct) {
            guard();
            var row = await repository.GetJobAsync(execution, ct);
            if (row is null || row.LeaseId != id || row.LeaseExpiresUtc != expires || expires <= clock.GetUtcNow() || row.State == "cancelled") throw new ManagementOperationConflictException();
        }
        public async Task RequireAsync(CancellationToken ct = default) {
            await JournalAsync(ct);
            if (NativeReady) {
                await policy(ct);
                foreach (var nativeFence in nativeFences) await nativeFence.RequireFencedAsync(execution, ct);
            }
            await JournalAsync(ct);
        }
    }
}
