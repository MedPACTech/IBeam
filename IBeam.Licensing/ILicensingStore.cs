namespace IBeam.Licensing;

public interface ILicensingStore
{
    Task<IReadOnlyList<TenantLicenseRecord>> ListLicensesAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantLicenseRecord?> GetLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<TenantLicenseRecord> UpsertLicenseAsync(TenantLicenseRecord license, CancellationToken ct = default);
    Task DeleteLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<LicenseSeatAssignmentInfo> AddAssignmentAsync(LicenseSeatAssignmentInfo assignment, CancellationToken ct = default);
    Task DeleteAssignmentAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct = default);
}
