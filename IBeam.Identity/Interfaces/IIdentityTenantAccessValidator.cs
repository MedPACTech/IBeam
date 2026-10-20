using IBeam.Identity.Models;
namespace IBeam.Identity.Interfaces;

public interface IIdentityTenantAccessValidator
{
    Task<IdentityTenantAccessState> RequireAsync(Guid userId, Guid tenantId, string? expectedVersion, CancellationToken ct = default);
}
public sealed record IdentityTenantAccessState(Guid UserId, Guid TenantId, string Version, IReadOnlyList<string> RoleNames, IReadOnlyList<Guid> RoleIds);
/// <summary>Explicit host integration contract: request authorization and refresh must enforce current tenant/membership state.</summary>
public interface IIdentityTenantAccessEnforcement
{
    int ProtocolVersion { get; }
    string Mode { get; }
}
public static class IdentityTenantAccessClaims
{
    public const string Version = "ibeam_tenant_access_version";
}
