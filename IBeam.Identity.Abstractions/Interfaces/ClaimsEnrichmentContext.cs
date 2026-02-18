namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public sealed record ClaimsEnrichmentContext(
    Guid UserId,
    Guid TenantId,
    IReadOnlyList<ClaimItem> CurrentClaims);
