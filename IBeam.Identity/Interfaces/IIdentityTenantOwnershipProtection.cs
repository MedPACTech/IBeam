using IBeam.Identity.Models;
namespace IBeam.Identity.Interfaces;

/// <summary>Trusted provider declaration: all native membership writers participate in the shared fence.</summary>
public interface IIdentityTenantOwnershipWriter
{
    int OwnershipProtocolVersion { get; }
    bool RequiresOwnershipProtection { get; }
}
public interface IIdentityTenantOwnershipConditionalStore : IIdentityTenantMembershipConditionalStore, IIdentityTenantOwnershipWriter
{
    Task ConfigureOwnershipProtectionAsync(Guid tenantId, IReadOnlyList<Guid> immutableOwnerRoleIds, CancellationToken ct = default);
}
public interface IIdentityTenantOwnershipRoleStore : ITenantRoleStore, IIdentityTenantOwnershipWriter
{
    /// <summary>Bounded exact-name lookup, including inactive/ambiguous definitions so services can reject them.</summary>
    Task<IReadOnlyList<TenantRole>> ReadOwnershipRolesAsync(Guid tenantId, IReadOnlyList<string> configuredNames, CancellationToken ct = default);
}
public interface IIdentityTenantOwnershipProtection
{
    int ProtocolVersion { get; }
    Task EnsureAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>Protocol 1 rejects global account disable/delete: no cross-tenant account mutation protocol is provided.</summary>
    Task RequireGlobalAccountMutationAsync(Guid userId, CancellationToken ct = default);
}
