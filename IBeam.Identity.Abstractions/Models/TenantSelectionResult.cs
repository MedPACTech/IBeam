namespace IBeam.Identity.Abstractions.Models;

public sealed record TenantSelectionResult(
    Guid UserId,
    Guid TenantId,
    IReadOnlyList<ClaimItem> IssuedClaims);
