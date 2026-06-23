namespace IBeam.Licensing.Services;

public sealed class LicenseSeatAssignmentService : ILicenseSeatAssignmentService
{
    private readonly ILicensingStore _store;

    public LicenseSeatAssignmentService(ILicensingStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        TenantLicenseService.ValidateLicenseId(licenseId);
        return await _store.ListAssignmentsAsync(tenantId, licenseId, ct).ConfigureAwait(false);
    }

    public async Task<LicenseSeatAssignmentInfo> AssignSeatAsync(
        Guid tenantId,
        Guid licenseId,
        AssignLicenseSeatRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        TenantLicenseService.ValidateLicenseId(licenseId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var license = await _store.GetLicenseAsync(tenantId, licenseId, ct).ConfigureAwait(false)
                      ?? throw new LicensingException($"License '{licenseId}' was not found for tenant '{tenantId}'.");

        if (!license.IsActive(DateTimeOffset.UtcNow))
            throw new LicensingException("Seats can only be assigned to active licenses.");

        var subject = ValidateSubject(request.Subject);
        var existing = await _store.ListAssignmentsAsync(tenantId, licenseId, ct).ConfigureAwait(false);

        var duplicate = existing.FirstOrDefault(x =>
            string.Equals(x.Subject.SubjectType, subject.SubjectType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Subject.SubjectId, subject.SubjectId, StringComparison.OrdinalIgnoreCase));

        if (duplicate is not null)
            return duplicate;

        if (license.SeatLimit is { } seatLimit && existing.Count >= seatLimit)
            throw new LicensingException($"License '{licenseId}' has reached its seat limit of {seatLimit}.");

        var assignment = new LicenseSeatAssignmentInfo(
            Guid.NewGuid(),
            tenantId,
            licenseId,
            subject,
            DateTimeOffset.UtcNow,
            createdByUserId == Guid.Empty ? null : createdByUserId,
            TenantLicenseService.NormalizeMetadata(request.Metadata));

        return await _store.AddAssignmentAsync(assignment, ct).ConfigureAwait(false);
    }

    public Task RevokeSeatAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct = default)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        TenantLicenseService.ValidateLicenseId(licenseId);
        if (assignmentId == Guid.Empty)
            throw new LicensingException("assignmentId is required.");

        return _store.DeleteAssignmentAsync(tenantId, licenseId, assignmentId, ct);
    }

    private static LicenseSubject ValidateSubject(LicenseSubject subject)
    {
        var type = TenantLicenseService.NormalizeRequired(subject.SubjectType, "subjectType");
        var id = TenantLicenseService.NormalizeRequired(subject.SubjectId, "subjectId");
        return new LicenseSubject(type, id, TenantLicenseService.NormalizeOptional(subject.DisplayName));
    }
}
