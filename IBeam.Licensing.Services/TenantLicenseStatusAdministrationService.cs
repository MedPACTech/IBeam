using IBeam.Services.Abstractions;

namespace IBeam.Licensing.Services;

/// <summary>Native licence rules over a fenced provider. Call only after the host's administration authorization boundary.</summary>
public sealed class TenantLicenseStatusAdministrationService : ITenantLicenseStatusAdministrationService
{
    private readonly ILicenseStatusConditionalStore store;
    private readonly ILicenseStatusRestorePolicy restore;
    private readonly IServiceOperationExecutor operations;
    private readonly TimeProvider clock;
    public TenantLicenseStatusAdministrationService(ILicenseStatusConditionalStore store, ILicenseStatusRestorePolicy restore,
        IServiceOperationExecutor operations, TimeProvider clock)
    {
        if (store.StatusProtocolVersion != 1 || !store.NativeWritersParticipate) throw new LicenseStatusUnavailableException();
        this.store = store; this.restore = restore; this.operations = operations; this.clock = clock;
    }
    public Task<LicenseStatusSnapshot> ReadAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
        => Run("licensing.licenses.status.read", tenantId, licenseId, async token =>
        {
            ValidateIds(tenantId, licenseId);
            var snapshot = await store.ReadStatusAsync(tenantId, licenseId, token) ?? throw new LicenseStatusConflictException();
            if (snapshot.License.TenantId != tenantId || snapshot.License.LicenseId != licenseId || !Exact(snapshot.Version))
                throw new LicenseStatusUnavailableException();
            return snapshot;
        }, ct);
    public Task<LicenseStatusReceipt?> FindAsync(Guid tenantId, Guid licenseId, Guid operationId, string authorityScope, string transition, CancellationToken ct = default)
        => Run<LicenseStatusReceipt?>("licensing.licenses.status.lookup", tenantId, licenseId, token =>
        {
            ValidateIds(tenantId, licenseId);
            if (operationId == Guid.Empty || !Scope(authorityScope) || !Transition(transition)) throw new LicenseStatusConflictException();
            return store.FindStatusReceiptAsync(tenantId, licenseId, operationId, authorityScope, transition, token);
        }, ct);
    public Task<LicenseStatusReceipt> ChangeAsync(LicenseStatusCommand command, CancellationToken ct = default)
        => Run("licensing.licenses.status.change", command?.TenantId ?? Guid.Empty, command?.LicenseId ?? Guid.Empty, async token =>
        {
            if (command is null || command.OperationId == Guid.Empty || !Scope(command.AuthorityScope) ||
                !Exact(command.ExpectedVersion) || !Transition(command.Transition)) throw new LicenseStatusConflictException();
            ValidateIds(command.TenantId, command.LicenseId);
            var intent = IntentHash(command);
            // Read-only reconciliation first; reusing a receipt never reapplies an old status after later native changes.
            var prior = await store.FindStatusReceiptAsync(command.TenantId, command.LicenseId, command.OperationId,
                command.AuthorityScope, command.Transition, token);
            if (prior is not null)
                return await store.CommitStatusAsync(new(command, intent, prior.Status, null), token);
            var current = await ReadAsync(command.TenantId, command.LicenseId, token);
            if (current.Version != command.ExpectedVersion)
            {
                // A concurrent request for this exact intent may have committed between the first lookup and this read.
                prior = await store.FindStatusReceiptAsync(command.TenantId, command.LicenseId, command.OperationId,
                    command.AuthorityScope, command.Transition, token);
                if (prior is not null) return await store.CommitStatusAsync(new(command, intent, prior.Status, null), token);
                throw new LicenseStatusPreconditionException();
            }
            string target; string? resume;
            if (command.Transition == "suspend")
            {
                if (!current.License.EvaluateRuntimeEligibility(clock.GetUtcNow()).IsEligible) throw new LicenseStatusConflictException();
                target = LicenseStatuses.Suspended; resume = current.License.Status.Trim().ToLowerInvariant();
            }
            else
            {
                if (!string.Equals(current.License.Status.Trim(), LicenseStatuses.Suspended, StringComparison.OrdinalIgnoreCase) || current.ResumeStatus is not
                    (LicenseStatuses.Active or LicenseStatuses.Trialing or LicenseStatuses.Grace or LicenseStatuses.Manual)) throw new LicenseStatusConflictException();
                target = current.ResumeStatus; resume = null;
                var candidate = current.License with { Status = target };
                if (!candidate.EvaluateRuntimeEligibility(clock.GetUtcNow()).IsEligible) throw new LicenseStatusConflictException();
                await restore.RequireAsync(command, current.License, candidate, token);
                if (!candidate.EvaluateRuntimeEligibility(clock.GetUtcNow()).IsEligible) throw new LicenseStatusConflictException();
            }
            return await store.CommitStatusAsync(new(command, intent, target, resume), token);
        }, ct);
    public static string IntentHash(LicenseStatusCommand command)
        => LicenseStatusFingerprint.Compute(command);
    public static bool Exact(string? version) => version is { Length: > 0 and <= 256 } && version != "*" && !version.Any(char.IsControl);
    private static bool Scope(string? scope) => scope is { Length: 64 } && scope.All(char.IsAsciiHexDigit);
    private static bool Transition(string? transition) => transition is "suspend" or "restore";
    private static void ValidateIds(Guid tenant, Guid license) { if (tenant == Guid.Empty || license == Guid.Empty) throw new LicenseStatusConflictException(); }
    private Task<T> Run<T>(string action, Guid tenant, Guid license, Func<CancellationToken, Task<T>> work, CancellationToken ct)
        => operations.ExecuteAsync(this, work, new ServiceOperationExecutionOptions { OperationName = action,
            TenantId = tenant, EntityId = license, PermissionEnabled = false, AuditEnabled = false }, ct);
}
