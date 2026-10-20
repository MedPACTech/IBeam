using System.Text.Json.Serialization;
namespace IBeam.Management.CustomerIdentity;

public sealed record ManagementAssignableRoleDto(Guid TenantId, Guid RoleId, string Name, IReadOnlyList<string> PolicyKeys);
public sealed record ManagementAssignableRoleReadEntity(Guid TenantId, Guid RoleId, string Name, bool IsActive);
public interface IManagementAssignableRoleReadRepository
{
    Task<IReadOnlyList<ManagementAssignableRoleReadEntity>> ReadAsync(Guid tenantId, IReadOnlyList<string> approvedNames, CancellationToken ct = default);
}
public interface IManagementAssignableRoleCatalogService
{
    Task<IReadOnlyList<ManagementAssignableRoleDto>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ManagementAssignableRoleDto>> RequireAsync(Guid tenantId, string policyKey, IReadOnlyList<Guid> roleIds, string authority, CancellationToken ct = default);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProvisionManagementCustomerUser
{
    public string Email { get; init; } = "";
    public string PolicyKey { get; init; } = "";
    public IReadOnlyList<Guid> RoleIds { get; init; } = [];
    public override string ToString() => "Customer provisioning request (recipient redacted)";
}
public sealed record ManagementCustomerProvisioningResult(ManagementIdentityOperationDto Operation, string Status, ManagementCustomerInvitationDto? Invitation);
public interface IManagementCustomerProvisioningService
{
    Task<ManagementCustomerProvisioningResult> ProvisionAsync(Guid tenantId, ProvisionManagementCustomerUser request, string key, CancellationToken ct = default);
}
/// <summary>Private target-only invitation intent/delivery journal. Recipient/token live only in ciphertext, not metadata or audit.</summary>
public sealed record ManagementCustomerProvisioningEntity
{
    public required ManagementTenantAdministrationScope Scope { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid InviteId { get; init; }
    public required Guid OriginalOperationId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required DateTimeOffset ExpiresUtc { get; init; }
    public string Stage { get; init; } = "reserved";
    public string DeliveryStatus { get; init; } = "pending";
    public DateTimeOffset? DeliveryLeaseUtc { get; init; }
    public Guid? DeliveryClaim { get; init; }
    public int DeliveryAttempts { get; init; }
    [JsonIgnore] public string Fingerprint { get; init; } = "";
    [JsonIgnore] public string EncryptedInput { get; init; } = "";
    public string Version { get; init; } = "";
    public override string ToString() => "Customer provisioning journal (private intent redacted)";
}
public interface IManagementCustomerProvisioningRepository
{
    Task LinkOperationAsync(ManagementTenantAdministrationScope scope, Guid tenantId, Guid operationId, Guid inviteId, CancellationToken ct = default);
    Task<ManagementCustomerProvisioningEntity?> GetForOperationAsync(ManagementTenantAdministrationScope scope, Guid tenantId, Guid operationId, CancellationToken ct = default);
    Task<ManagementCustomerProvisioningEntity?> GetAsync(ManagementTenantAdministrationScope scope, Guid tenantId, Guid inviteId, CancellationToken ct = default);
    Task<bool> TryCreateAsync(ManagementCustomerProvisioningEntity entity, CancellationToken ct = default);
    Task<ManagementCustomerProvisioningEntity> ReplaceAsync(ManagementCustomerProvisioningEntity entity, string expectedVersion, CancellationToken ct = default);
}
