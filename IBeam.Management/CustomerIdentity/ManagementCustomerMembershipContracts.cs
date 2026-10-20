using System.Text.Json.Serialization;
namespace IBeam.Management.CustomerIdentity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeManagementCustomerMembership { public required bool IsEnabled { get; init; } }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeManagementCustomerRoles
{
    public string PolicyKey { get; init; } = "";
    public IReadOnlyList<Guid> RoleIds { get; init; } = [];
}
public sealed record ManagementCustomerMembershipResult(ManagementIdentityOperationDto Operation, ManagementCustomerUserDto? Membership);
public sealed record ManagementPortalCustomerMembershipResult(ManagementPortalOperationResult Tracking, ManagementCustomerUserDto? Membership);
public interface IManagementCustomerMembershipService
{
    Task<ManagementCustomerMembershipResult> SetEnabledAsync(Guid tenantId, Guid userId, ChangeManagementCustomerMembership request, string version, string key, CancellationToken ct = default);
    Task<ManagementCustomerMembershipResult> SetRolesAsync(Guid tenantId, Guid userId, ChangeManagementCustomerRoles request, string version, string key, CancellationToken ct = default);
}
public interface IManagementPortalCustomerMembershipService
{
    Task<ManagementPortalCustomerMembershipResult> SetEnabledAsync(Guid appId, Guid tenantId, Guid userId, ChangeManagementCustomerMembership request, string version, string key, CancellationToken ct = default);
    Task<ManagementPortalCustomerMembershipResult> SetRolesAsync(Guid appId, Guid tenantId, Guid userId, ChangeManagementCustomerRoles request, string version, string key, CancellationToken ct = default);
}
