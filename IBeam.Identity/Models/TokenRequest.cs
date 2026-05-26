namespace IBeam.Identity.Models;

public sealed record TokenRequest(
    Guid UserId,
    Guid TenantId,
    IReadOnlyList<ClaimItem>? AdditionalClaims = null);
