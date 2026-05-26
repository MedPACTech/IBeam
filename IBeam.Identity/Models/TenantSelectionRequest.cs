namespace IBeam.Identity.Models;

public sealed record TenantSelectionRequest(
    Guid UserId,
    Guid TenantId,
    bool SetAsDefault);
