namespace IBeam.Identity.Abstractions.Models;

public sealed record TenantSelectionRequest(
    Guid UserId,
    Guid TenantId);

public sealed record TenantSelectionResult(
    Guid UserId,
    Guid TenantId,
    IReadOnlyList<ClaimItem> IssuedClaims);
