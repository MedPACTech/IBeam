using IBeam.Services.Abstractions;

namespace IBeam.Licensing.Services;

/// <summary>Trusted native seat rules. The Management adapter supplies live authorization and metadata audit.</summary>
public sealed class TenantLicenseSeatAdministrationService : ITenantLicenseSeatAdministrationService
{
    public const int MaximumAssignments = LicenseSeatProtocol.MaximumAssignments;
    private readonly ILicenseSeatConditionalStore store;
    private readonly ILicenseSeatSubjectPolicy subjects;
    private readonly IServiceOperationExecutor operations;
    private readonly TimeProvider clock;
    public TenantLicenseSeatAdministrationService(ILicenseSeatConditionalStore store, ILicenseSeatSubjectPolicy subjects,
        IServiceOperationExecutor operations, TimeProvider clock)
    {
        if (store.SeatProtocolVersion != 1 || !store.NativeSeatWritersParticipate) throw new LicenseSeatUnavailableException();
        this.store = store; this.subjects = subjects; this.operations = operations; this.clock = clock;
    }
    public Task<LicenseSeatSnapshot> ReadAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
        => Run("read", tenantId, licenseId, async token =>
        {
            if (tenantId == Guid.Empty || licenseId == Guid.Empty) throw new LicenseSeatConflictException();
            var value = await store.ReadSeatsAsync(tenantId, licenseId, MaximumAssignments, token) ?? throw new LicenseSeatConflictException();
            if (value.License.TenantId != tenantId || value.License.LicenseId != licenseId || !TenantLicenseStatusAdministrationService.Exact(value.Version) ||
                value.Assignments.Count > MaximumAssignments || value.Assignments.Any(x => x.TenantId != tenantId || x.LicenseId != licenseId || x.AssignmentId == Guid.Empty) ||
                value.Assignments.Select(x => x.AssignmentId).Distinct().Count() != value.Assignments.Count) throw new LicenseSeatUnavailableException();
            return value;
        }, ct);
    public Task<LicenseSeatReceipt?> FindAsync(Guid tenantId, Guid licenseId, Guid operationId, string scope, string transition, CancellationToken ct = default)
        => Run<LicenseSeatReceipt?>("lookup", tenantId, licenseId, token =>
        {
            if (tenantId == Guid.Empty || licenseId == Guid.Empty || operationId == Guid.Empty || !Scope(scope) || !Transition(transition)) throw new LicenseSeatConflictException();
            return store.FindSeatReceiptAsync(tenantId, licenseId, operationId, scope, transition, token);
        }, ct);
    public Task<LicenseSeatReceipt> ChangeAsync(LicenseSeatCommand command, CancellationToken ct = default)
        => Run("change", command?.TenantId ?? Guid.Empty, command?.LicenseId ?? Guid.Empty, async token =>
        {
            if (command is null || command.TenantId == Guid.Empty || command.LicenseId == Guid.Empty || command.UserId == Guid.Empty || command.OperationId == Guid.Empty ||
                !Scope(command.AuthorityScope) || !Transition(command.Transition) || !TenantLicenseStatusAdministrationService.Exact(command.ExpectedVersion) ||
                command.Transition == "assign" && command.AssignmentId.HasValue ||
                command.Transition == "unassign" && (!command.AssignmentId.HasValue || command.AssignmentId.Value == Guid.Empty))
                throw new LicenseSeatConflictException();
            var hash = LicenseSeatFingerprint.Compute(command);
            async Task<LicenseSeatReceipt?> Prior()
            {
                var receipt = await store.FindSeatReceiptAsync(command.TenantId, command.LicenseId, command.OperationId, command.AuthorityScope, command.Transition, token);
                return receipt is null ? null : await store.CommitSeatAsync(new(command, hash, null, null, receipt.Disposition), token);
            }
            if (await Prior() is { } prior) return prior;
            var snapshot = await ReadAsync(command.TenantId, command.LicenseId, token);
            if (snapshot.Version != command.ExpectedVersion)
            {
                if (await Prior() is { } concurrent) return concurrent;
                throw new LicenseSeatPreconditionException();
            }
            await subjects.RequireAsync(command.TenantId, command.UserId, command.Transition, token);
            if (command.Transition == "assign")
            {
                if (!snapshot.License.EvaluateRuntimeEligibility(clock.GetUtcNow()).IsEligible) throw new LicenseSeatConflictException();
                var existing = snapshot.Assignments.Where(x => User(x, command.UserId)).ToArray();
                if (existing.Length > 1) throw new LicenseSeatConflictException();
                if (existing.Length == 1) return await store.CommitSeatAsync(new(command, hash, existing[0], null, "already-assigned"), token);
                if (snapshot.Assignments.Count >= MaximumAssignments || snapshot.License.SeatLimit is { } limit && snapshot.Assignments.Count >= limit)
                    throw new LicenseSeatCapacityException();
                var assignment = new LicenseSeatAssignmentInfo(LicenseSeatFingerprint.AssignmentId(command), command.TenantId, command.LicenseId,
                    new(LicenseSubjectTypes.User, command.UserId.ToString("D")), clock.GetUtcNow(), null, new Dictionary<string, string>());
                return await store.CommitSeatAsync(new(command, hash, assignment, null, "assigned"), token);
            }
            var selected = snapshot.Assignments.SingleOrDefault(x => x.AssignmentId == command.AssignmentId);
            if (selected is not null && !User(selected, command.UserId)) throw new LicenseSeatConflictException();
            return await store.CommitSeatAsync(new(command, hash, null, command.AssignmentId, selected is null ? "not-assigned" : "unassigned"), token);
        }, ct);
    internal static bool User(LicenseSeatAssignmentInfo assignment, Guid userId) => LicenseSeatFingerprint.MatchesUser(assignment, userId);
    private static bool Scope(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool Transition(string? value) => value is "assign" or "unassign";
    private Task<T> Run<T>(string action, Guid tenant, Guid license, Func<CancellationToken, Task<T>> body, CancellationToken ct)
        => operations.ExecuteAsync(this, body, new ServiceOperationExecutionOptions { OperationName = "licensing.seats.administration." + action,
            TenantId = tenant, EntityId = license, PermissionEnabled = false, AuditEnabled = false }, ct);
}
