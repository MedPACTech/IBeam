namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface IClaimsEnricher
{
    Task<IReadOnlyList<ClaimItem>> EnrichAsync(ClaimsEnrichmentContext context, CancellationToken ct = default);
}
