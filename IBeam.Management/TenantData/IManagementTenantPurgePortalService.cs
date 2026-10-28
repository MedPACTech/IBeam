using System.Text.Json.Serialization;

namespace IBeam.Management.TenantData;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantPurgePlanAction(string PolicyKey);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantPurgeConfirmAction(string PolicyKey, Guid PlanId, string ConfirmationBinding);
public interface IManagementTenantPurgePortalService
{
    Task<ManagementTenantPurgeResult> PlanAsync(Guid applicationId, Guid tenantId, ManagementTenantPurgeRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<ManagementTenantPurgeResult> ConfirmAsync(Guid applicationId, Guid tenantId, ManagementTenantPurgeRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<ManagementTenantPurgeResult> FindAsync(Guid applicationId, Guid tenantId, Guid jobId, CancellationToken ct = default);
}
