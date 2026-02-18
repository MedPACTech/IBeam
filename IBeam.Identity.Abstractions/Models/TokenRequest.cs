namespace IBeam.Identity.Abstractions.Models;

public sealed record TokenRequest(
    Guid UserId,
    Guid TenantId,
    IReadOnlyList<ClaimItem>? AdditionalClaims = null);
