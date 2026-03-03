namespace IBeam.Identity.Models;

public sealed record TenantSelectionResult(
    Guid UserId,
    Guid TenantId,
    IReadOnlyList<ClaimItem> IssuedClaims);
