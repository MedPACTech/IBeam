namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

public interface IClaimsEnricher
{
    Task<IReadOnlyList<ClaimItem>> EnrichAsync(ClaimsEnrichmentContext context, CancellationToken ct = default);
}
