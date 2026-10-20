namespace IBeam.Management.CustomerIdentity;

public sealed record ManagementPortalCustomerInvitationResult(ManagementPortalOperationResult Tracking, string Status, ManagementCustomerInvitationDto? Invitation)
{ public override string ToString() => "Portal customer invitation result (recipient/reference redacted)"; }
public interface IManagementPortalCustomerProvisioningService
{
    Task<IReadOnlyList<ManagementAssignableRoleDto>> ListRolesAsync(Guid applicationId, Guid tenantId, CancellationToken ct = default);
    Task<ManagementPortalCustomerInvitationResult> ProvisionAsync(Guid applicationId, Guid tenantId, ProvisionManagementCustomerUser request, string key, CancellationToken ct = default);
    Task<ManagementPortalCustomerInvitationResult> ResendAsync(Guid applicationId, Guid tenantId, Guid inviteId, string key, string version, CancellationToken ct = default);
    Task<ManagementPortalCustomerInvitationResult> RevokeAsync(Guid applicationId, Guid tenantId, Guid inviteId, string key, string version, CancellationToken ct = default);
}
