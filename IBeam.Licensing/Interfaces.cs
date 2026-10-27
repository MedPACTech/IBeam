namespace IBeam.Licensing;

public interface ILicensePlanCatalogProvider
{
    Task<IReadOnlyList<LicensePlanInfo>> ListPlansAsync(CancellationToken ct = default);
    Task<LicensePlanInfo?> GetPlanAsync(string planKey, CancellationToken ct = default);
}

public interface ILicenseProductCatalogProvider
{
    Task<IReadOnlyList<LicenseProductInfo>> ListProductsAsync(CancellationToken ct = default);
    Task<LicenseProductInfo?> GetProductAsync(string productKey, CancellationToken ct = default);
}

public interface ITenantLicenseService
{
    int StatusAdministrationProtocolVersion => 0;
    /// <summary>Optional trusted administration protocol; unsupported providers fail closed.</summary>
    Task<LicenseStatusSnapshot> ReadStatusAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
        => throw new LicenseStatusUnavailableException();
    Task<LicenseStatusReceipt> ChangeStatusAsync(LicenseStatusCommand command, CancellationToken ct = default)
        => throw new LicenseStatusUnavailableException();
    Task<LicenseStatusReceipt?> FindStatusOperationAsync(Guid tenantId, Guid licenseId, Guid operationId,
        string authorityScope, string transition, CancellationToken ct = default) => throw new LicenseStatusUnavailableException();
    Task<IReadOnlyList<TenantLicenseInfo>> ListTenantLicensesAsync(Guid tenantId, CancellationToken ct = default);
    async Task<TenantLicenseInfo?> GetLicenseByKeyAsync(Guid tenantId, Guid licenseKey, CancellationToken ct = default)
    {
        if (licenseKey == Guid.Empty)
            throw new LicensingException("licenseKey is required.");

        var licenses = await ListTenantLicensesAsync(tenantId, ct).ConfigureAwait(false);
        return licenses.FirstOrDefault(x => x.LicenseKey == licenseKey);
    }
    Task<TenantLicenseInfo> GrantLicenseAsync(Guid tenantId, GrantTenantLicenseRequest request, Guid? createdByUserId = null, CancellationToken ct = default);
    Task<TenantLicenseInfo> UpdateLicenseAsync(Guid tenantId, Guid licenseId, UpdateTenantLicenseRequest request, CancellationToken ct = default);
    Task RevokeLicenseAsync(Guid tenantId, Guid licenseId, string? reason, CancellationToken ct = default);
}

public interface ILicenseSeatAssignmentService
{
    int SeatAdministrationProtocolVersion => 0;
    Task<LicenseSeatSnapshot> ReadManagementSeatsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default) => throw new LicenseSeatUnavailableException();
    Task<LicenseSeatReceipt> ChangeManagementSeatAsync(LicenseSeatCommand command, CancellationToken ct = default) => throw new LicenseSeatUnavailableException();
    Task<LicenseSeatReceipt?> FindManagementSeatOperationAsync(Guid tenantId, Guid licenseId, Guid operationId, string scope, string transition, CancellationToken ct = default)
        => throw new LicenseSeatUnavailableException();
    Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<LicenseSeatAssignmentInfo> AssignSeatAsync(Guid tenantId, Guid licenseId, AssignLicenseSeatRequest request, Guid? createdByUserId = null, CancellationToken ct = default);
    Task RevokeSeatAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct = default);
}

public interface ILicenseSeatPolicyService
{
    Task<TenantLicenseWithSeatsInfo> GrantSingleUserLicenseAsync(
        Guid tenantId,
        GrantSingleUserLicenseRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default);

    Task<TenantLicenseWithSeatsInfo> GrantTenantSeatLicenseAsync(
        Guid tenantId,
        GrantTenantSeatLicenseRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default);
}

public interface ILicenseAuthorizer
{
    Task<LicenseAuthorizationResult> AuthorizeAsync(
        Guid tenantId,
        LicenseSubject subject,
        string entitlement,
        CancellationToken ct = default);
}

public interface ILicenseGate
{
    Task<LicenseGateResult> CheckAsync(LicenseGateRequest request, CancellationToken ct = default);
    Task RequireAsync(LicenseGateRequest request, CancellationToken ct = default);
}

public interface ILicenseRuntimeContextService
{
    Task<LicenseRuntimeContextInfo> GetRuntimeContextAsync(
        Guid tenantId,
        GetLicenseRuntimeContextRequest request,
        CancellationToken ct = default);
}

public interface ILicenseSubjectResolver
{
    LicenseSubject? ResolveSubject(System.Security.Claims.ClaimsPrincipal? principal);
}

public interface ILicenseExtension
{
    Guid TenantId { get; }
    Guid LicenseId { get; }
}
