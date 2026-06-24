using Microsoft.Extensions.Options;

namespace IBeam.AccessControl.Services;

public sealed class ResourceAccessAuthorizer : IResourceAccessAuthorizer
{
    private readonly IResourceAccessStore _store;
    private readonly IOptions<AccessControlOptions> _options;

    public ResourceAccessAuthorizer(
        IResourceAccessStore store,
        IOptions<AccessControlOptions> options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ResourceAccessAuthorizationResult> AuthorizeAsync(
        Guid tenantId,
        string resourceType,
        string resourceId,
        AccessSubject subject,
        string requiredAccessLevel,
        CancellationToken ct = default)
    {
        ResourceAccessService.ValidateTenantId(tenantId);

        var normalizedResourceType = ResourceAccessService.NormalizeRequired(resourceType, nameof(resourceType));
        var normalizedResourceId = ResourceAccessService.NormalizeRequired(resourceId, nameof(resourceId));
        var normalizedSubject = ResourceAccessService.NormalizeSubject(subject);
        var normalizedRequiredLevel = ResourceAccessService.NormalizeRequired(requiredAccessLevel, nameof(requiredAccessLevel));
        var grants = await _store.ListGrantsAsync(tenantId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        foreach (var grant in grants.Where(x => x.IsActive(now)))
        {
            if (!ResourceMatches(grant, normalizedResourceType, normalizedResourceId))
                continue;
            if (!ResourceAccessService.SubjectMatches(grant.Subject, normalizedSubject))
                continue;
            if (!AccessLevelAllows(grant.AccessLevel, normalizedRequiredLevel))
                continue;

            return ResourceAccessAuthorizationResult.Allow(grant.GrantId, grant.AccessLevel);
        }

        return ResourceAccessAuthorizationResult.Deny(
            $"Subject '{normalizedSubject.SubjectType}:{normalizedSubject.SubjectId}' does not have '{normalizedRequiredLevel}' access to '{normalizedResourceType}:{normalizedResourceId}'.");
    }

    private static bool ResourceMatches(ResourceAccessGrantRecord grant, string resourceType, string resourceId)
        => string.Equals(grant.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase) &&
           (string.Equals(grant.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(grant.ResourceId, "*", StringComparison.OrdinalIgnoreCase));

    private bool AccessLevelAllows(string granted, string required)
    {
        var ranks = _options.Value.AccessLevelRanks;
        if (string.Equals(granted, required, StringComparison.OrdinalIgnoreCase))
            return true;

        if (ranks.TryGetValue(granted, out var grantedRank) &&
            ranks.TryGetValue(required, out var requiredRank))
            return grantedRank >= requiredRank;

        return string.Equals(granted, "*", StringComparison.OrdinalIgnoreCase);
    }
}
