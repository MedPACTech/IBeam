using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantUserProvisioningService
{
    Task<ProvisionTenantUserResult> ProvisionAsync(
        Guid tenantId,
        ProvisionTenantUserRequest request,
        Guid provisionedByUserId,
        CancellationToken ct = default);
}

