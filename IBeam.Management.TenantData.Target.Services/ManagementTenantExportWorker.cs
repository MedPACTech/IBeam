using System.Security.Claims;
using System.Text.Json;
using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.TenantData.Target.Services;

/// <summary>Trusted target worker. Providers must reconcile by stable job ID and fence publication/delivery before committing effects.</summary>
public sealed class ManagementTenantExportWorker(IManagementTenantExportJobRepository repository, IManagementTenantExportHandler handler,
    IManagementTenantExportDelivery delivery, IManagementTenantExportPolicy policy, IManagementAuditRepository audit,
    IOptions<ManagementAuditOptions> auditOptions, IOptions<ManagementTenantExportJobOptions> options, TimeProvider clock,
    IServiceOperationExecutor operations, IOptions<ManagementDiagnosticsOptions> deployment, IOptions<ManagementOperationTargetOptions> target) : IManagementTenantExportWorker
{
    private readonly ManagementTenantExportArtifactProtection protection = new(options.Value);
    public Task RunAsync(ManagementTenantExportExecution execution, bool reconcileUncertain = false, CancellationToken ct = default)
        => Run(execution, async token =>
        {
            var initial = await Load(execution, token);
            if (initial.CancelRequested || clock.GetUtcNow() >= initial.ExpiresUtc || initial.Phase == "done" || initial.NeedsReconciliation && !reconcileUncertain) return;
            var row = await Claim(initial, token); if (row is null) return;
            var lease = new Fence(repository, row.Execution, row.LeaseId!.Value, row.LeaseExpiresUtc!.Value, clock, cleanup: false);
            var native = row.Execution with { LeaseFence = lease, ArtifactExpiresUtc = row.ExpiresUtc };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(row.LeaseExpiresUtc.Value - clock.GetUtcNow());
            try
            {
                await policy.RequireStatusAsync(row.Execution, deadline.Token);
                var current = ManagementTenantExportProtocol.Definition(handler.Describe());
                if (JsonSerializer.Serialize(current) != JsonSerializer.Serialize(row.Definition)) throw new ManagementOperationUnavailableException();
                if (row.Phase == "export")
                {
                    await lease.RequireAsync(deadline.Token); await Record(row, false, deadline.Token);
                    var output = await handler.ExecuteOrResumeAsync(native, deadline.Token);
                    await lease.RequireAsync(deadline.Token);
                    if (output?.Artifact is null || output.Artifact.ExpiresUtc.Offset != TimeSpan.Zero || output.Artifact.ExpiresUtc <= clock.GetUtcNow() || output.Artifact.ExpiresUtc > row.ExpiresUtc)
                        throw new ManagementTenantExportPermanentFailureException("handler-failed");
                    await delivery.RequireArtifactAsync(native, output.Artifact, deadline.Token);
                    var effective = row with { ExpiresUtc = output.Artifact.ExpiresUtc };
                    var cipher = protection.Protect(effective, output.Artifact);
                    ManagementTenantExportCoverage[] coverage;
                    try
                    {
                        var state = output.Coverage.All(x => x.Disposition == "completed") ? ManagementTenantExportStates.Succeeded : ManagementTenantExportStates.PartiallyCompleted;
                        var status = new ManagementTenantExportJobStatus(1, row.Execution.Authority.TenantId, row.Execution.JobId, state,
                            row.CreatedUtc, clock.GetUtcNow(), effective.ExpiresUtc, row.Definition.Consistency, output.Coverage.ToArray(), null);
                        coverage = ManagementTenantExportProtocol.Result(new(status.TenantId, status.JobId, "found", status), status.TenantId, status.JobId, row.Definition).Job!.Coverage;
                    }
                    catch
                    {
                        row = await Save(effective with { ProtectedArtifact = cipher, State = ManagementTenantExportStates.Failed, Phase = "done", FailureCode = "handler-failed" }, row, lease, deadline.Token, release: true);
                        await Record(row, false, deadline.Token); return;
                    }
                    row = await Save(effective with { ProtectedArtifact = cipher, Coverage = coverage, Phase = "delivery", State = ManagementTenantExportStates.Running }, row, lease, deadline.Token);
                }
                if (row.Phase == "delivery")
                {
                    await lease.RequireAsync(deadline.Token); await policy.RequireStatusAsync(row.Execution, deadline.Token);
                    var artifact = protection.Unprotect(row);
                    if (artifact.ExpiresUtc <= clock.GetUtcNow()) throw new ManagementTenantExportPermanentFailureException("delivery-denied");
                    await delivery.RequireArtifactAsync(native, artifact, deadline.Token);
                    await delivery.DeliverOrReconcileAsync(native, artifact, deadline.Token);
                    await lease.RequireAsync(deadline.Token);
                    row = await Save(row with { Phase = "done", State = row.Coverage.All(x => x.Disposition == "completed") ? ManagementTenantExportStates.Succeeded : ManagementTenantExportStates.PartiallyCompleted,
                        NeedsReconciliation = false, FailureCode = null }, row, lease, deadline.Token, release: true);
                    await Record(row, true, deadline.Token);
                }
            }
            catch (ManagementOperationConflictException) { }
            catch (ManagementAuditUnavailableException) { throw; }
            catch (Exception ex)
            {
                // Cancellation/crashes cannot prove that an external provider did not
                // commit. Only a positive Pending signal permits unattended resumption.
                var pending = ex is ManagementTenantExportPendingException;
                var permanent = ex as ManagementTenantExportPermanentFailureException;
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var latest = await Load(execution, recovery.Token);
                    if (latest.LeaseId != row.LeaseId || latest.CancelRequested || latest.LeaseExpiresUtc <= clock.GetUtcNow()) return;
                    row = await Save(latest with { NeedsReconciliation = !pending && permanent is null, State = permanent is null ? ManagementTenantExportStates.Running : ManagementTenantExportStates.Failed,
                        Phase = permanent is null ? latest.Phase : "done", FailureCode = permanent?.Code ?? (pending ? null : "unknown") }, latest, lease, recovery.Token, release: true);
                    await Record(row, false, recovery.Token);
                }
                catch (ManagementOperationConflictException) { }
                catch { throw new ManagementOperationUnavailableException(); }
            }
        }, ct);

    public Task CancelAsync(ManagementTenantExportExecution execution, CancellationToken ct = default)
        => Run(execution, async token =>
        {
            var row = await Load(execution, token, cleanup: true);
            if (row.Cleaned || row.CancelRequested) return;
            await repository.ReplaceAsync(row with { CancelRequested = true, State = ManagementTenantExportStates.Cancelled, FailureCode = "cancelled", UpdatedUtc = clock.GetUtcNow() }, row.Version, token);
            await Record(row, true, token);
        }, ct);

    public Task CleanupAsync(ManagementTenantExportExecution execution, CancellationToken ct = default)
        => Run(execution, async token =>
        {
            var initial = await Load(execution, token, cleanup: true);
            if (initial.Cleaned || !initial.CancelRequested && initial.State != ManagementTenantExportStates.Failed && clock.GetUtcNow() < initial.ExpiresUtc) return;
            var row = await Claim(initial, token); if (row is null) return;
            var lease = new Fence(repository, row.Execution, row.LeaseId!.Value, row.LeaseExpiresUtc!.Value, clock, cleanup: true);
            var native = row.Execution with { LeaseFence = lease, ArtifactExpiresUtc = row.ExpiresUtc };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(row.LeaseExpiresUtc.Value - clock.GetUtcNow());
            try
            {
                await lease.RequireAsync(deadline.Token); await Record(row, false, deadline.Token);
                await handler.CancelAsync(native, deadline.Token);
                if (!string.IsNullOrEmpty(row.ProtectedArtifact))
                {
                    var artifact = protection.Unprotect(row);
                    await delivery.RequireArtifactAsync(native, artifact, deadline.Token);
                    await delivery.RevokeAndDeleteAsync(native, artifact, deadline.Token);
                }
                row = await Save(row with { Cleaned = true, ProtectedArtifact = "", Phase = "done", NeedsReconciliation = false,
                    State = row.CancelRequested ? ManagementTenantExportStates.Cancelled : row.State == ManagementTenantExportStates.Failed ? row.State : ManagementTenantExportStates.Expired,
                    FailureCode = row.CancelRequested ? "cancelled" : row.State == ManagementTenantExportStates.Failed ? row.FailureCode : "expired" }, row, lease, deadline.Token, release: true);
                await Record(row, true, deadline.Token);
            }
            catch (ManagementOperationConflictException) { }
            catch
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var latest = await Load(execution, recovery.Token, cleanup: true);
                    if (latest.LeaseId == row.LeaseId && latest.LeaseExpiresUtc > clock.GetUtcNow())
                        await Save(latest with { NeedsReconciliation = true, FailureCode = "unknown" }, latest, lease, recovery.Token, release: true);
                }
                catch (ManagementOperationConflictException) { }
                catch { }
                throw new ManagementOperationUnavailableException();
            }
        }, ct);

    private async Task<ManagementTenantExportJobEntity?> Claim(ManagementTenantExportJobEntity row, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (row.LeaseId.HasValue && row.LeaseExpiresUtc > now) return null;
        try { return await repository.ReplaceAsync(row with { LeaseId = Guid.NewGuid(), LeaseExpiresUtc = now.AddSeconds(options.Value.LeaseSeconds), UpdatedUtc = now,
            State = row.State == ManagementTenantExportStates.Requested ? ManagementTenantExportStates.Running : row.State }, row.Version, ct); }
        catch (ManagementOperationConflictException) { return null; }
    }
    private async Task<ManagementTenantExportJobEntity> Save(ManagementTenantExportJobEntity next, ManagementTenantExportJobEntity previous, Fence lease, CancellationToken ct, bool release = false)
    {
        await lease.RequireAsync(ct);
        return await repository.ReplaceAsync(next with { UpdatedUtc = clock.GetUtcNow(), LeaseId = release ? null : previous.LeaseId, LeaseExpiresUtc = release ? null : previous.LeaseExpiresUtc }, previous.Version, ct);
    }
    private async Task<ManagementTenantExportJobEntity> Load(ManagementTenantExportExecution execution, CancellationToken ct, bool cleanup = false)
    { ManagementTenantExportDeploymentGuard.Require(execution, deployment.Value, target.Value, requireWorkAuthority: !cleanup); return await repository.GetAsync(execution, ct) ?? throw new ManagementAccessDeniedException(); }
    private async Task Record(ManagementTenantExportJobEntity row, bool succeeded, CancellationToken ct)
    {
        try
        {
            var now = clock.GetUtcNow();
            await audit.AppendAsync(new ManagementAuditEntity { AuditId = Guid.NewGuid(), OccurredUtc = now, ExpiresUtc = now.AddDays(auditOptions.Value.RetentionDays),
                OrganizationId = row.Execution.Authority.OrganizationId, ApplicationId = row.Execution.Authority.ApplicationId,
                SubjectId = row.Execution.JobId, SubjectType = "system", ApplicationKey = row.Execution.ApplicationKey, Environment = row.Execution.Environment,
                Operation = "management.tenants.export.worker", Succeeded = succeeded, CorrelationId = row.Execution.JobId.ToString("N") }, ct);
        }
        catch { throw new ManagementAuditUnavailableException(); }
    }
    private Task Run(ManagementTenantExportExecution execution, Func<CancellationToken, Task> action, CancellationToken ct)
        => operations.ExecuteAsync(this, async token =>
        {
            try { await action(token); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationConflictException or ManagementOperationUnavailableException or ManagementAuditUnavailableException) &&
                !(ex is OperationCanceledException && token.IsCancellationRequested)) { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.tenants.export.worker", AuditAction = "management.tenants.export.worker",
            EntityId = execution.Authority.TenantId, EntityName = "ManagementTenantExportJob", PermissionEnabled = false, AuditEnabled = true }, ct);

    private sealed class Fence(IManagementTenantExportJobRepository store, ManagementTenantExportExecution execution, Guid id, DateTimeOffset expires, TimeProvider clock, bool cleanup)
        : IManagementTenantExportLeaseFence
    {
        public DateTimeOffset ExpiresUtc => expires;
        public async Task RequireAsync(CancellationToken ct = default)
        {
            var row = await store.GetAsync(execution, ct);
            if (row is null || row.LeaseId != id || row.LeaseExpiresUtc != expires || expires <= clock.GetUtcNow() ||
                !cleanup && (row.CancelRequested || row.ExpiresUtc <= clock.GetUtcNow())) throw new ManagementOperationConflictException();
        }
    }
}

public sealed class ManagementTenantExportCustomerArtifacts(IManagementTenantExportJobRepository repository, IManagementTenantExportCustomerAuthorization customers,
    IManagementTenantExportDelivery delivery, IOptions<ManagementTenantExportJobOptions> options, TimeProvider clock,
    IServiceOperationExecutor operations, IOptions<ManagementDiagnosticsOptions> deployment, IOptions<ManagementOperationTargetOptions> target) : IManagementTenantExportCustomerArtifacts
{
    private readonly ManagementTenantExportArtifactProtection protection = new(options.Value);
    public Task<ManagementTenantExportArtifact> RequireAsync(ManagementTenantExportExecution execution, ClaimsPrincipal authenticatedCustomer, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            try
            {
                ManagementTenantExportDeploymentGuard.Require(execution, deployment.Value, target.Value);
                var identities = authenticatedCustomer.Identities.Where(x => x.IsAuthenticated).ToArray();
                if (identities.Length != 1 || identities[0].Claims.Any(x => x.Type == ManagementClaimTypes.SubjectType && x.Value is ManagementSubjectTypes.Human or ManagementSubjectTypes.Credential))
                    throw new ManagementAccessDeniedException();
                await customers.RequireAsync(execution.Authority.RealmId, execution.Authority.TenantId, execution.JobId, authenticatedCustomer, token);
                var row = await repository.GetAsync(execution, token) ?? throw new ManagementAccessDeniedException();
                Ready(row);
                var artifact = protection.Unprotect(row);
                if (artifact.ExpiresUtc <= clock.GetUtcNow()) throw new ManagementAccessDeniedException();
                await delivery.RequireArtifactAsync(row.Execution, artifact, token);
                await customers.RequireAsync(execution.Authority.RealmId, execution.Authority.TenantId, execution.JobId, authenticatedCustomer, token);
                Ready(await repository.GetAsync(execution, token) ?? throw new ManagementAccessDeniedException());
                return artifact;
            }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationUnavailableException) && !(ex is OperationCanceledException && token.IsCancellationRequested))
            { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.tenants.export.customer-retrieve", AuditAction = "management.tenants.export.customer-retrieve",
            EntityId = execution.Authority.TenantId, EntityName = "ManagementTenantExportArtifact", PermissionEnabled = false, AuditEnabled = true }, ct);
    private void Ready(ManagementTenantExportJobEntity row)
    {
        if (row.CancelRequested || row.Cleaned || row.ExpiresUtc <= clock.GetUtcNow() || row.Phase != "done" ||
            row.State is not (ManagementTenantExportStates.Succeeded or ManagementTenantExportStates.PartiallyCompleted)) throw new ManagementAccessDeniedException();
    }
}
