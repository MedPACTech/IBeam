namespace IBeam.Management.CustomerIdentity;

/// <summary>Opaque short-lived reconciliation reference, never a claim that an unknown mutation failed.</summary>
public sealed record ManagementPortalOperationResult(string Reference, string Status, ManagementIdentityOperationDto? Operation, DateTimeOffset ExpiresUtc)
{ public override string ToString() => "Portal operation tracking (reference redacted)"; }
public sealed record ManagementPortalTenantMutationResult(ManagementPortalOperationResult Tracking, ManagementCustomerTenantDto? Tenant,
    string? Source, string? ExternalReference)
{ public override string ToString() => "Portal tenant mutation (identity fields redacted)"; }
public interface IManagementPortalTenantAdministrationService
{
    Task<ManagementPortalTenantMutationResult> CreateAsync(Guid applicationId, CreateManagementCustomerTenant request, string idempotencyKey, CancellationToken ct = default);
    Task<ManagementPortalTenantMutationResult> RenameAsync(Guid applicationId, Guid tenantId, RenameManagementCustomerTenant request,
        string expectedVersion, string idempotencyKey, CancellationToken ct = default);
}
public interface IManagementPortalMutationOperationService
{
    Task<ManagementPortalOperationResult> GetAsync(Guid applicationId, string reference, string authority, CancellationToken ct = default);
}
