namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

public sealed record ClaimsEnrichmentContext(
    Guid UserId,
    Guid TenantId,
    IReadOnlyList<ClaimItem> CurrentClaims);
