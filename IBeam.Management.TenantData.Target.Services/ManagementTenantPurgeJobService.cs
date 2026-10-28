using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.TenantData.Target.Services;

public sealed class ManagementTenantPurgeJobService(IManagementTenantPurgeJournalRepository repository, IManagementOperationAccessService access,
    IManagementTenantPurgeHandler handler, IManagementTenantPurgePolicy policy, IEnumerable<IManagementTenantPurgeAccessFence> fences,
    IOptions<ManagementTenantPurgeOptions> module, IOptions<ManagementDiagnosticsOptions> deployment, IOptions<ManagementOperationTargetOptions> target,
    TimeProvider clock, IServiceOperationExecutor operations) : IManagementTenantPurgeJobService
{
    public int ProtocolVersion => 1;
    public bool Durable => repository.Durable;
    public Task<ManagementTenantPurgePlan> CreatePlanAsync(ManagementTenantPurgeExecution execution, ManagementTenantPurgePlan proposal, CancellationToken ct = default)
        => Run(execution, async token => {
            var definition = ManagementTenantPurgeProtocol.Definition(handler.Describe());
            var verified = ManagementTenantPurgeProtocol.Plan(proposal, execution, definition, clock.GetUtcNow());
            if (verified.PlanId != execution.JobId || !module.Value.PolicyKeys.Contains(verified.PolicyKey, StringComparer.Ordinal) ||
                execution.Authority.ResourceVersion != verified.ExpectedVersion) throw new ManagementOperationInvalidException();
            var keys = FenceKeys(fences); var intent = Hash(new { verified.PolicyKey, verified.ExpectedVersion, verified.InventoryFingerprint, FenceKeys = keys });
            var existing = await repository.GetPlanAsync(execution, verified.PlanId, token);
            if (existing is not null) return ExistingPlan(existing, intent, execution, definition);
            await policy.RequireAsync(execution, verified.ExpectedVersion, token);
            await access.RequireAsync(ManagementTenantDataOperations.Delete, execution.Authority, token);
            var entity = new ManagementTenantPurgeJournalEntity { Kind = "plan", Execution = execution with { LeaseFence = null }, Definition = definition, Plan = verified,
                IntentHash = intent, CreatedUtc = verified.CreatedUtc, UpdatedUtc = verified.CreatedUtc, FenceKeys = keys };
            if (!await repository.TryCreatePlanAsync(entity, token)) {
                existing = await repository.GetPlanAsync(execution, verified.PlanId, token) ?? throw new ManagementOperationUnavailableException();
                return ExistingPlan(existing, intent, execution, definition);
            }
            return verified;
        }, ct, lookup: true);
    private ManagementTenantPurgePlan ExistingPlan(ManagementTenantPurgeJournalEntity row, string intent, ManagementTenantPurgeExecution execution, ManagementTenantPurgeDefinition definition)
    {
        if (row.IntentHash != intent || row.ConsumedJobId is not null) throw new ManagementOperationConflictException();
        return ManagementTenantPurgeProtocol.Plan(row.Plan, execution, definition, clock.GetUtcNow());
    }
    public Task<ManagementTenantPurgePlan?> ReadPlanAsync(ManagementTenantPurgeExecution execution, Guid planId, CancellationToken ct = default)
        => Run(execution, async token => {
            var row = await repository.GetPlanAsync(execution, planId, token);
            return row is null ? null : row.Plan with { Categories = row.Plan.Categories.Select(x => x with { }).ToArray() };
        }, ct, lookup: true);
    internal Task<ManagementTenantPurgeResult?> FindPriorConfirmationAsync(ManagementTenantPurgeExecution execution, ManagementTenantPurgeRequest request, CancellationToken ct)
        => Run(execution, async token => {
            var row = await repository.GetJobAsync(execution, token);
            if (row is null) return null;
            if (row.Plan.PlanId != request.PlanId || row.Plan.ConfirmationBinding != request.ConfirmationBinding ||
                row.Plan.ExpectedVersion != request.ExpectedVersion || row.Plan.PolicyKey != request.PolicyKey) throw new ManagementOperationConflictException();
            await policy.RequireStatusAsync(row.Execution, token);
            return Project(row, "accepted", clock.GetUtcNow());
        }, ct, lookup: true);
    public Task<ManagementTenantPurgeResult> ConfirmAsync(ManagementTenantPurgeExecution execution, ManagementTenantPurgePlan plan, CancellationToken ct = default)
        => Run(execution, async token => {
            var intent = Hash(new { plan.PlanId, plan.ConfirmationBinding, plan.ExpectedVersion, plan.PolicyKey, plan.InventoryFingerprint });
            var existing = await repository.GetJobAsync(execution, token);
            if (existing is not null) {
                if (existing.IntentHash != intent) throw new ManagementOperationConflictException();
                await policy.RequireStatusAsync(existing.Execution, token); return Project(existing, "accepted", clock.GetUtcNow());
            }
            var stored = await repository.GetPlanAsync(execution, plan.PlanId, token) ?? throw new ManagementOperationPreconditionException();
            if (stored.ConsumedJobId == execution.JobId) {
                // Another identical confirmer may have committed after our initial job read but before this plan read.
                // Recover that atomic receipt before evaluating fresh-plan expiry/inventory preconditions.
                var committed = await repository.GetJobAsync(execution, token) ?? throw new ManagementOperationUnavailableException();
                if (committed.IntentHash != intent) throw new ManagementOperationConflictException();
                await policy.RequireStatusAsync(committed.Execution, token);
                return Project(committed, "accepted", clock.GetUtcNow());
            }
            var definition = ManagementTenantPurgeProtocol.Definition(handler.Describe());
            var verified = ManagementTenantPurgeProtocol.Plan(stored.Plan, execution, definition, clock.GetUtcNow());
            var keys = FenceKeys(fences);
            if (stored.ConsumedJobId is not null || plan.PlanId == execution.JobId || verified.ConfirmationBinding != plan.ConfirmationBinding ||
                verified.ExpectedVersion != plan.ExpectedVersion || execution.Authority.ResourceVersion != plan.ExpectedVersion || !stored.FenceKeys.SequenceEqual(keys) ||
                !module.Value.PolicyKeys.Contains(plan.PolicyKey, StringComparer.Ordinal)) throw new ManagementOperationPreconditionException();
            await policy.RequireAsync(execution, plan.ExpectedVersion, token);
            await access.RequireAsync(ManagementTenantDataOperations.Delete, execution.Authority, token);
            var now = clock.GetUtcNow();
            var native = execution with { PolicyKey = verified.PolicyKey, InventoryFingerprint = verified.InventoryFingerprint, LeaseFence = null };
            var job = new ManagementTenantPurgeJournalEntity { Kind = "job", Execution = native, Definition = stored.Definition, Plan = verified, IntentHash = intent,
                CreatedUtc = now, UpdatedUtc = now, FenceKeys = keys, Coverage = verified.Categories.Select(x => x with {
                    Disposition = x.Disposition switch { "delete" => "pending", "retain" => "retained", _ => "excluded" } }).ToArray() };
            // This single provider transaction consumes the exact plan and adds the stable job.
            // The worker must still conditionally acquire all native revision/write fences before any deletion.
            if (!await repository.TryConfirmAsync(stored with { ConsumedJobId = execution.JobId, UpdatedUtc = now }, stored.Version, job, token)) {
                existing = await repository.GetJobAsync(execution, token);
                if (existing is null || existing.IntentHash != intent) throw new ManagementOperationConflictException();
                return Project(existing, "accepted", clock.GetUtcNow());
            }
            return Project(job, "accepted", clock.GetUtcNow());
        }, ct);
    public Task<ManagementTenantPurgeResult> FindAsync(ManagementTenantPurgeExecution execution, CancellationToken ct = default)
        => Run(execution, async token => {
            var row = await repository.GetJobAsync(execution, token);
            if (row is null) return new(execution.Authority.TenantId, execution.JobId, "unknown", null, null);
            await policy.RequireStatusAsync(row.Execution, token);
            return Project(row, "found", clock.GetUtcNow());
        }, ct, lookup: true);
    internal static ManagementTenantPurgeResult Project(ManagementTenantPurgeJournalEntity row, string outcome, DateTimeOffset now)
    {
        var e = row.Execution;
        var stale = row.NeedsReconciliation && row.State != "failed" && (row.LeaseId is null || row.LeaseExpiresUtc <= now);
        return ManagementTenantPurgeProtocol.Result(new(e.Authority.TenantId, e.JobId, outcome, null,
            new(1, e.Authority.TenantId, e.JobId, row.Plan.PlanId, stale ? "unknown" : row.State, row.CreatedUtc, row.UpdatedUtc, row.Coverage, stale ? "unknown" : row.FailureCode)), e, row.Definition);
    }
    internal static string[] FenceKeys(IEnumerable<IManagementTenantPurgeAccessFence> fences)
    {
        var array = fences.ToArray();
        if (array.Length is < 1 or > 8 || array.Any(x => x.ProtocolVersion != 1 || !ManagementTenantExportMetadata.Key(x.Key)) ||
            array.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != array.Length) throw new ManagementOperationUnavailableException();
        return array.Select(x => x.Key).Order(StringComparer.Ordinal).ToArray();
    }
    internal static void Guard(ManagementTenantPurgeExecution e, ManagementDiagnosticsOptions deployment, ManagementOperationTargetOptions target)
    {
        var s = e.Authority;
        if (s.OrganizationId == Guid.Empty || s.ApplicationId == Guid.Empty || s.RealmId == Guid.Empty || s.TenantId == Guid.Empty || s.ActorUserId == Guid.Empty ||
            e.JobId == Guid.Empty || s.OperationId != e.JobId || s.ResourceId != s.TenantId || e.ApplicationKey != deployment.ApplicationKey || e.Environment != deployment.Environment ||
            string.IsNullOrWhiteSpace(e.ApplicationKey) || e.ApplicationKey.Length > 100 || e.ApplicationKey.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(e.Environment) || e.Environment.Length > 100 || e.Environment.Any(char.IsControl) || s.RealmId != target.RealmId ||
            !target.DedicatedApplicationRealm || !target.EnabledOperations.Contains(ManagementTenantDataOperations.Delete, StringComparer.Ordinal) ||
            !target.TrustedPortals.Any(x => x.OrganizationId == s.OrganizationId && x.ApplicationId == s.ApplicationId)) throw new ManagementAccessDeniedException();
    }
    private Task<T> Run<T>(ManagementTenantPurgeExecution e, Func<CancellationToken, Task<T>> action, CancellationToken ct, bool lookup = false)
        => operations.ExecuteAsync(this, async token => {
            try {
                Guard(e, deployment.Value, target.Value);
                await access.RequireAsync(ManagementTenantDataOperations.Delete, e.Authority, token);
                var result = await action(token);
                Guard(e, deployment.Value, target.Value);
                await access.RequireAsync(ManagementTenantDataOperations.Delete, e.Authority, token);
                return result;
            } catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationConflictException or ManagementOperationInvalidException or ManagementOperationPreconditionException or ManagementOperationUnavailableException) &&
                !(ex is OperationCanceledException && token.IsCancellationRequested)) { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = lookup ? "management.operations.lookup" : ManagementTenantDataOperations.Delete,
            AuditAction = lookup ? "management.operations.lookup" : ManagementTenantDataOperations.Delete, EntityId = e.Authority.TenantId,
            EntityName = "ManagementTenantPurgeJournal", PermissionEnabled = false, AuditEnabled = true }, ct);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
