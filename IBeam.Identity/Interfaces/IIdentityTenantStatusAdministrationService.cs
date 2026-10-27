using IBeam.Identity.Models;
namespace IBeam.Identity.Interfaces;
public interface IIdentityTenantStatusConditionalStore
{
    Task<VersionedIdentityTenant> SetStatusConditionalAsync(Guid tenantId, bool active, string expectedVersion, Guid operationId, CancellationToken ct = default);
}
public interface IIdentityTenantStatusAdministrationService
{
    bool SupportsConditionalStatus { get; }
    Task SetStatusAsync(Guid tenantId, bool active, string expectedVersion, Guid operationId, TenantExtensionContext context, CancellationToken ct = default);
    Task ResumeStatusAsync(Guid tenantId, bool active, TenantExtensionContext context, CancellationToken ct = default);
}
