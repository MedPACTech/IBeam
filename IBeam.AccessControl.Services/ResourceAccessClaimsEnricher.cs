using System.Text.Json;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.Extensions.Options;

namespace IBeam.AccessControl.Services;

public sealed class ResourceAccessClaimsEnricher : IClaimsEnricher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IResourceAccessService _access;
    private readonly IOptions<AccessControlOptions> _options;

    public ResourceAccessClaimsEnricher(
        IResourceAccessService access,
        IOptions<AccessControlOptions> options)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<ClaimItem>> EnrichAsync(
        ClaimsEnrichmentContext context,
        CancellationToken ct = default)
    {
        if (!_options.Value.EmitResourceAccessClaim)
            return [];

        if (context.UserId == Guid.Empty || context.TenantId == Guid.Empty)
            return [];

        var subject = new AccessSubject(AccessSubjectTypes.User, context.UserId.ToString("D"));
        var grants = await _access.ListGrantsAsync(context.TenantId, subject: subject, ct: ct)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var activeGrants = grants
            .Where(x => string.Equals(x.Status, ResourceAccessGrantStatuses.Active, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.ExpiresUtc is null || x.ExpiresUtc > now)
            .OrderBy(x => x.ResourceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AccessLevel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var max = Math.Max(1, _options.Value.MaxResourceAccessClaimsInJwt);
        var payload = new ResourceAccessJwtPayload(
            Truncated: activeGrants.Count > max,
            Grants: activeGrants
                .Take(max)
                .Select(x => new ResourceAccessJwtGrant(
                    x.ResourceType,
                    x.ResourceId,
                    x.AccessLevel,
                    x.ExpiresUtc))
                .ToList());

        return
        [
            new ClaimItem(
                ResourceAccessClaimTypes.ResourceAccess,
                JsonSerializer.Serialize(payload, JsonOptions),
                "json")
        ];
    }

    private sealed record ResourceAccessJwtPayload(
        bool Truncated,
        IReadOnlyList<ResourceAccessJwtGrant> Grants);

    private sealed record ResourceAccessJwtGrant(
        string ResourceType,
        string ResourceId,
        string AccessLevel,
        DateTimeOffset? ExpiresUtc);
}
