namespace IBeam.Licensing;

public interface ILicensePlanCatalogProvider
{
    Task<IReadOnlyList<LicensePlanInfo>> ListPlansAsync(CancellationToken ct = default);
    Task<LicensePlanInfo?> GetPlanAsync(string planKey, CancellationToken ct = default);
}

public interface ITenantLicenseService
{
    Task<IReadOnlyList<TenantLicenseInfo>> ListTenantLicensesAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantLicenseInfo> GrantLicenseAsync(Guid tenantId, GrantTenantLicenseRequest request, Guid? createdByUserId = null, CancellationToken ct = default);
    Task<TenantLicenseInfo> UpdateLicenseAsync(Guid tenantId, Guid licenseId, UpdateTenantLicenseRequest request, CancellationToken ct = default);
    Task RevokeLicenseAsync(Guid tenantId, Guid licenseId, string? reason, CancellationToken ct = default);
}

public interface ILicenseSeatAssignmentService
{
    Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<LicenseSeatAssignmentInfo> AssignSeatAsync(Guid tenantId, Guid licenseId, AssignLicenseSeatRequest request, Guid? createdByUserId = null, CancellationToken ct = default);
    Task RevokeSeatAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct = default);
}

public interface ILicenseAuthorizer
{
    Task<LicenseAuthorizationResult> AuthorizeAsync(
        Guid tenantId,
        LicenseSubject subject,
        string entitlement,
        CancellationToken ct = default);
}

public interface ILicenseExtension
{
    Guid TenantId { get; }
    Guid LicenseId { get; }
}
