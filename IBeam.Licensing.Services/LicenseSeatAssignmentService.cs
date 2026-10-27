using IBeam.Services.Abstractions;

namespace IBeam.Licensing.Services;

[IBeamOperation("licensing.seats")]
public sealed class LicenseSeatAssignmentService : ILicenseSeatAssignmentService
{
    private readonly ILicensingStore _store;
    private readonly IServiceOperationExecutor _operations;
    private readonly ITenantLicenseSeatAdministrationService? _management;
    private readonly ILicenseSeatSubjectPolicy? _subjects;
    public int SeatAdministrationProtocolVersion => _management is null ? 0 : 1;
    public LicenseSeatAssignmentService(ILicensingStore store, IServiceOperationExecutor operations, ITenantLicenseSeatAdministrationService management)
        : this(store, operations) => _management = management;
    public LicenseSeatAssignmentService(ILicensingStore store, IServiceOperationExecutor operations, ITenantLicenseSeatAdministrationService management,
        ILicenseSeatSubjectPolicy subjects) : this(store, operations, management) => _subjects = subjects;
    public Task<LicenseSeatSnapshot> ReadManagementSeatsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
        => (_management ?? throw new LicenseSeatUnavailableException()).ReadAsync(tenantId, licenseId, ct);
    public Task<LicenseSeatReceipt> ChangeManagementSeatAsync(LicenseSeatCommand command, CancellationToken ct = default)
        => (_management ?? throw new LicenseSeatUnavailableException()).ChangeAsync(command, ct);
    public Task<LicenseSeatReceipt?> FindManagementSeatOperationAsync(Guid tenantId, Guid licenseId, Guid operationId, string scope, string transition, CancellationToken ct = default)
        => (_management ?? throw new LicenseSeatUnavailableException()).FindAsync(tenantId, licenseId, operationId, scope, transition, ct);

    public LicenseSeatAssignmentService(ILicensingStore store, IServiceOperationExecutor? operations = null)
    {
        _store = store;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("licensing.seats.list")]
    public async Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListAssignmentsCoreAsync(tenantId, licenseId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = licenseId },
            ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsCoreAsync(Guid tenantId, Guid licenseId, CancellationToken ct)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        TenantLicenseService.ValidateLicenseId(licenseId);
        return await _store.ListAssignmentsAsync(tenantId, licenseId, ct).ConfigureAwait(false);
    }

    [IBeamOperation("licensing.seats.assign")]
    public async Task<LicenseSeatAssignmentInfo> AssignSeatAsync(
        Guid tenantId,
        Guid licenseId,
        AssignLicenseSeatRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => AssignSeatCoreAsync(tenantId, licenseId, request, createdByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = licenseId },
            ct).ConfigureAwait(false);

    private async Task<LicenseSeatAssignmentInfo> AssignSeatCoreAsync(
        Guid tenantId,
        Guid licenseId,
        AssignLicenseSeatRequest request,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        TenantLicenseService.ValidateLicenseId(licenseId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var conditional = _store as ILicenseSeatConditionalStore;
        var snapshot = conditional is { SeatProtocolVersion: 1, NativeSeatWritersParticipate: true } &&
            await conditional.RequiresSeatProtectionAsync(tenantId, licenseId, ct).ConfigureAwait(false)
                ? await conditional.ReadSeatsAsync(tenantId, licenseId, TenantLicenseSeatAdministrationService.MaximumAssignments, ct).ConfigureAwait(false) : null;
        var license = snapshot?.License ?? await _store.GetLicenseAsync(tenantId, licenseId, ct).ConfigureAwait(false)
                      ?? throw new LicensingException($"License '{licenseId}' was not found for tenant '{tenantId}'.");

        var eligibility = license.EvaluateRuntimeEligibility(DateTimeOffset.UtcNow);
        if (!eligibility.IsEligible)
            throw new LicensingException($"Seats can only be assigned to runtime-eligible licenses. {eligibility.Reason}");

        var subject = ValidateSubject(request.Subject);
        if (snapshot is not null) await RequireManagedUserAsync(tenantId, subject, "assign", ct).ConfigureAwait(false);
        var existing = snapshot?.Assignments ?? await _store.ListAssignmentsAsync(tenantId, licenseId, ct).ConfigureAwait(false);

        var duplicate = existing.FirstOrDefault(x =>
            string.Equals(x.Subject.SubjectType, subject.SubjectType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Subject.SubjectId, subject.SubjectId, StringComparison.OrdinalIgnoreCase));

        if (duplicate is not null)
            return duplicate;

        if (license.SeatLimit is { } seatLimit && existing.Count >= seatLimit)
            throw new LicensingException($"License '{licenseId}' has reached its seat limit of {seatLimit}.");
        if (snapshot is not null && existing.Count >= TenantLicenseSeatAdministrationService.MaximumAssignments) throw new LicenseSeatCapacityException();

        var assignment = new LicenseSeatAssignmentInfo(
            Guid.NewGuid(),
            tenantId,
            licenseId,
            subject,
            DateTimeOffset.UtcNow,
            createdByUserId == Guid.Empty ? null : createdByUserId,
            TenantLicenseService.NormalizeMetadata(request.Metadata));

        if (snapshot is not null)
        {
            await conditional!.ApplyNativeSeatAsync(tenantId, licenseId, snapshot.Version, assignment, null, ct).ConfigureAwait(false);
            return assignment;
        }
        return await _store.AddAssignmentAsync(assignment, ct).ConfigureAwait(false);
    }

    [IBeamOperation("licensing.seats.revoke")]
    public Task RevokeSeatAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => RevokeSeatCoreAsync(tenantId, licenseId, assignmentId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = assignmentId },
            ct);

    private async Task RevokeSeatCoreAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        TenantLicenseService.ValidateLicenseId(licenseId);
        if (assignmentId == Guid.Empty)
            throw new LicensingException("assignmentId is required.");

        if (_store is ILicenseSeatConditionalStore { SeatProtocolVersion: 1, NativeSeatWritersParticipate: true } conditional &&
            await conditional.RequiresSeatProtectionAsync(tenantId, licenseId, ct).ConfigureAwait(false))
        {
            var snapshot = await conditional.ReadSeatsAsync(tenantId, licenseId, TenantLicenseSeatAdministrationService.MaximumAssignments, ct).ConfigureAwait(false)
                ?? throw new LicenseSeatConflictException();
            if (!snapshot.Assignments.Any(x => x.AssignmentId == assignmentId)) return;
            await RequireManagedUserAsync(tenantId, snapshot.Assignments.Single(x => x.AssignmentId == assignmentId).Subject, "unassign", ct).ConfigureAwait(false);
            await conditional.ApplyNativeSeatAsync(tenantId, licenseId, snapshot.Version, null, assignmentId, ct).ConfigureAwait(false);
            return;
        }
        await _store.DeleteAssignmentAsync(tenantId, licenseId, assignmentId, ct).ConfigureAwait(false);
    }

    private static LicenseSubject ValidateSubject(LicenseSubject subject)
    {
        var type = TenantLicenseService.NormalizeRequired(subject.SubjectType, "subjectType");
        var id = TenantLicenseService.NormalizeRequired(subject.SubjectId, "subjectId");
        return new LicenseSubject(type, id, TenantLicenseService.NormalizeOptional(subject.DisplayName));
    }
    private async Task RequireManagedUserAsync(Guid tenantId, LicenseSubject subject, string transition, CancellationToken ct)
    {
        if (!string.Equals(subject.SubjectType, LicenseSubjectTypes.User, StringComparison.OrdinalIgnoreCase)) return;
        if (_subjects is null || !Guid.TryParseExact(subject.SubjectId, "D", out var userId) || userId == Guid.Empty) throw new LicenseSeatConflictException();
        await _subjects.RequireAsync(tenantId, userId, transition, ct).ConfigureAwait(false);
    }
}
