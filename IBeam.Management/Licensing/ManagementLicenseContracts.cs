namespace IBeam.Management.Licensing;

public static class ManagementLicenseOperations
{
    public const string Read = "management.licenses.read";
    public const string Suspend = "management.licenses.suspend";
    public const string Restore = "management.licenses.restore";
}
public sealed record ManagementLicenseReadRequest;
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementLicenseActionRequest;
public sealed record ManagementLicenseSuspendRequest(Guid LicenseId, string ExpectedVersion);
public sealed record ManagementLicenseRestoreRequest(Guid LicenseId, string ExpectedVersion);
/// <summary>Safe operational metadata; no billing profiles, provider keys, entitlement expansion or member records.</summary>
public sealed record ManagementLicenseStatus(Guid TenantId, Guid LicenseId, string Status, string Version);
public sealed record ManagementLicenseTransitionResult(Guid TenantId, Guid LicenseId, Guid OperationId,
    string Outcome, string? Status, string? Version);
public interface IManagementLicensePortalService
{
    Task<ManagementLicenseStatus> ReadAsync(Guid applicationId, Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<ManagementLicenseTransitionResult> SuspendAsync(Guid applicationId, Guid tenantId, ManagementLicenseSuspendRequest request,
        string idempotencyKey, CancellationToken ct = default);
    Task<ManagementLicenseTransitionResult> RestoreAsync(Guid applicationId, Guid tenantId, ManagementLicenseRestoreRequest request,
        string idempotencyKey, CancellationToken ct = default);
    Task<ManagementLicenseTransitionResult> FindAsync(Guid applicationId, Guid tenantId, Guid licenseId, Guid operationId,
        string operation, CancellationToken ct = default);
}
