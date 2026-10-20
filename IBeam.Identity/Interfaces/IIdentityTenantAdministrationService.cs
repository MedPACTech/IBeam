using IBeam.Identity.Models;
namespace IBeam.Identity.Interfaces;

/// <summary>Additive optional concurrency port. Existing tenant stores remain compatible.</summary>
public interface IIdentityTenantConditionalStore
{
    Task<VersionedIdentityTenant?> FindVersionedAsync(Guid tenantId, CancellationToken ct = default);
    Task<VersionedIdentityTenant> RenameAsync(Guid tenantId, string name, string expectedVersion, Guid operationId, CancellationToken ct = default);
}
public sealed record VersionedIdentityTenant(IdentityTenant Tenant, string? Version, Guid? LastOperationId)
{ public override string ToString() => "Versioned Identity tenant (fields redacted)"; }
/// <summary>Opt-in external administration through existing Identity lifecycle hooks. No membership/selection/token operations.</summary>
public interface IIdentityTenantAdministrationService
{
    bool SupportsConditionalUpdates { get; }
    Task<VersionedIdentityTenant?> FindAsync(Guid tenantId, CancellationToken ct = default);
    Task CreateAsync(string name, Guid tenantId, TenantExtensionContext context, CancellationToken ct = default);
    Task ResumeCreationAsync(Guid tenantId, TenantExtensionContext context, CancellationToken ct = default);
    Task RenameAsync(Guid tenantId, string name, string expectedVersion, Guid operationId, TenantExtensionContext context, CancellationToken ct = default);
    Task ResumeRenameAsync(Guid tenantId, TenantExtensionContext context, CancellationToken ct = default);
}
