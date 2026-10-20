using IBeam.Identity.Models;
namespace IBeam.Identity.Interfaces;

/// <summary>Optional provider port. Snapshot/version conditions cover every supported writer in one tenant partition.</summary>
public interface IIdentityTenantMembershipConditionalStore
{
    Task ConfigureProtectionAsync(Guid tenantId, IReadOnlyList<Guid> immutableAdministratorRoleIds, CancellationToken ct = default);
    Task<IdentityTenantMembershipSnapshot> ReadSnapshotAsync(Guid tenantId, int maximumMembers, CancellationToken ct = default);
    Task<IdentityTenantMembershipState?> FindStateAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<IdentityTenantMembershipState> ReplaceStateAsync(IdentityTenantMembershipState next, string expectedMemberVersion, string expectedTenantVersion, Guid operationId, CancellationToken ct = default);
    Task RepairProjectionAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
public sealed record IdentityTenantMembershipState(Guid TenantId, Guid UserId, bool IsEnabled, IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNames, string Version, Guid? LastOperationId);
public sealed record IdentityTenantMembershipSnapshot(Guid TenantId, string Version, IReadOnlyList<Guid> ProtectedAdministratorRoleIds, IReadOnlyList<IdentityTenantMembershipState> Members);
public interface IIdentityTenantMembershipAdministrationService
{
    Task<IdentityTenantMembershipState> SetEnabledAsync(Guid tenantId, Guid userId, bool enabled, string version, Guid operationId, IReadOnlyList<Guid> administratorRoleIds, CancellationToken ct = default);
    Task<IdentityTenantMembershipState> SetRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<TenantRole> roles, string version, Guid operationId, IReadOnlyList<Guid> administratorRoleIds, CancellationToken ct = default);
    Task<UserTenantRoleAssignment?> RevokeProtectedRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default);
}
