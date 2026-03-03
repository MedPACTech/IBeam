namespace IBeam.Identity.Models;

public sealed record SelectTenantRequest(Guid TenantId,
    bool SetAsDefault);
