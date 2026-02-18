namespace IBeam.Identity.Abstractions.Models;

public sealed record SelectTenantRequest(Guid TenantId,
    bool SetAsDefault);
