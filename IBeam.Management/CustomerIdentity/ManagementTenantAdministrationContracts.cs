using System.Text.Json.Serialization;
using IBeam.Management.Entities;
namespace IBeam.Management.CustomerIdentity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateManagementCustomerTenant
{
    public string Name { get; init; } = "";
    public string? ExternalReference { get; init; }
    public override string ToString() => "Create customer tenant (fields redacted)";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RenameManagementCustomerTenant
{
    public string Name { get; init; } = "";
    public override string ToString() => "Rename customer tenant (fields redacted)";
}
public sealed record ManagementTenantMutationResult(ManagementIdentityOperationDto Operation, ManagementCustomerTenantDto? Tenant,
    string Source, string? ExternalReference)
{ public override string ToString() => "Tenant operation result (identity fields redacted)"; }
public sealed record ManagementTenantAdministrationScope(Guid IdentityRealmId, string ApplicationKey, string Environment);
public interface IManagementTenantAdministrationRepository
{
    Task<ManagementTenantAdministrationEntity?> GetAsync(ManagementTenantAdministrationScope scope, Guid tenantId, CancellationToken ct = default);
    /// <summary>Atomically reserve the tenant and optional case-insensitive external reference in the target realm.</summary>
    Task<bool> TryCreateAsync(ManagementTenantAdministrationEntity entity, CancellationToken ct = default);
    Task<ManagementTenantAdministrationEntity> ReplaceAsync(ManagementTenantAdministrationEntity entity, string expectedVersion, CancellationToken ct = default);
}
public interface IManagementTenantAdministrationService
{
    Task<ManagementTenantMutationResult> CreateAsync(CreateManagementCustomerTenant request, string idempotencyKey, CancellationToken ct = default);
    Task<ManagementTenantMutationResult> RenameAsync(Guid tenantId, RenameManagementCustomerTenant request, string expectedVersion, string idempotencyKey, CancellationToken ct = default);
}
/// <summary>Presence advertises an actually usable optional conditional Identity adapter.</summary>
public interface IManagementTenantRenameCapability;
public sealed class ManagementCustomerIdentityPreconditionException() : Exception("The tenant version has changed; read its current version before retrying.");
