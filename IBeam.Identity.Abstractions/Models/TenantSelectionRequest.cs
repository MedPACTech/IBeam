namespace IBeam.Identity.Abstractions.Models;

public sealed record TenantSelectionRequest(
    Guid UserId,
    Guid TenantId);
