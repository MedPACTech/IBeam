using Microsoft.Extensions.Options;

namespace IBeam.AccessControl.Services;

public sealed class ResourceAccessAuthorizer : IResourceAccessAuthorizer
{
    private readonly IResourceAccessStore _store;
    private readonly IResourceAccessHierarchyResolver _hierarchy;
    private readonly IOptions<AccessControlOptions> _options;

    public ResourceAccessAuthorizer(
        IResourceAccessStore store,
        IResourceAccessHierarchyResolver hierarchy,
        IOptions<AccessControlOptions> options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
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
        var resourcesToCheck = await BuildResourceSetAsync(
            tenantId,
            normalizedResourceType,
            normalizedResourceId,
            ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        foreach (var grant in grants.Where(x => x.IsActive(now)))
        {
            if (!ResourceMatches(grant, resourcesToCheck))
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

    private async Task<IReadOnlyList<ResourceAccessResource>> BuildResourceSetAsync(
        Guid tenantId,
        string resourceType,
        string resourceId,
        CancellationToken ct)
    {
        var resources = new List<ResourceAccessResource>
        {
            new(resourceType, resourceId)
        };

        var ancestors = await _hierarchy.ListAncestorsAsync(tenantId, resourceType, resourceId, ct)
            .ConfigureAwait(false);

        foreach (var ancestor in ancestors)
        {
            var normalized = new ResourceAccessResource(
                ResourceAccessService.NormalizeRequired(ancestor.ResourceType, nameof(ancestor.ResourceType)),
                ResourceAccessService.NormalizeRequired(ancestor.ResourceId, nameof(ancestor.ResourceId)));

            if (!resources.Any(x => ResourceEquals(x, normalized)))
                resources.Add(normalized);
        }

        return resources;
    }

    private static bool ResourceMatches(ResourceAccessGrantRecord grant, IReadOnlyList<ResourceAccessResource> resources)
        => resources.Any(resource =>
            string.Equals(grant.ResourceType, resource.ResourceType, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(grant.ResourceId, resource.ResourceId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(grant.ResourceId, "*", StringComparison.OrdinalIgnoreCase)));

    private static bool ResourceEquals(ResourceAccessResource left, ResourceAccessResource right)
        => string.Equals(left.ResourceType, right.ResourceType, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.ResourceId, right.ResourceId, StringComparison.OrdinalIgnoreCase);

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
