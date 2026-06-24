namespace IBeam.AccessControl.Services;

public sealed class ResourceAccessService : IResourceAccessService
{
    private readonly IResourceAccessStore _store;

    public ResourceAccessService(IResourceAccessStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<ResourceAccessGrantInfo>> ListGrantsAsync(
        Guid tenantId,
        string? resourceType = null,
        string? resourceId = null,
        AccessSubject? subject = null,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var grants = await _store.ListGrantsAsync(tenantId, ct).ConfigureAwait(false);
        var query = grants.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            var normalizedResourceType = NormalizeRequired(resourceType, nameof(resourceType));
            query = query.Where(x => string.Equals(x.ResourceType, normalizedResourceType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            var normalizedResourceId = NormalizeRequired(resourceId, nameof(resourceId));
            query = query.Where(x => string.Equals(x.ResourceId, normalizedResourceId, StringComparison.OrdinalIgnoreCase));
        }

        if (subject is not null)
        {
            var normalizedSubject = NormalizeSubject(subject);
            query = query.Where(x => SubjectMatches(x.Subject, normalizedSubject));
        }

        return query.Select(ResourceAccessGrantInfo.FromRecord).ToList();
    }

    public async Task<ResourceAccessGrantInfo> GrantAccessAsync(
        Guid tenantId,
        GrantResourceAccessRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var grant = new ResourceAccessGrantRecord(
            GrantId: Guid.NewGuid(),
            TenantId: tenantId,
            ResourceType: NormalizeRequired(request.ResourceType, nameof(request.ResourceType)),
            ResourceId: NormalizeRequired(request.ResourceId, nameof(request.ResourceId)),
            Subject: NormalizeSubject(request.Subject),
            AccessLevel: NormalizeRequired(request.AccessLevel, nameof(request.AccessLevel)),
            Status: ResourceAccessGrantStatuses.Active,
            CreatedUtc: now,
            CreatedByUserId: createdByUserId,
            UpdatedUtc: null,
            ExpiresUtc: request.ExpiresUtc,
            Metadata: NormalizeMetadata(request.Metadata));

        return ResourceAccessGrantInfo.FromRecord(await _store.UpsertGrantAsync(grant, ct).ConfigureAwait(false));
    }

    public async Task<ResourceAccessGrantInfo> UpdateGrantAsync(
        Guid tenantId,
        Guid grantId,
        UpdateResourceAccessGrantRequest request,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateGrantId(grantId);
        ArgumentNullException.ThrowIfNull(request);

        var existing = await _store.GetGrantAsync(tenantId, grantId, ct).ConfigureAwait(false)
            ?? throw new AccessControlException($"Resource access grant '{grantId}' was not found.");

        var updated = existing with
        {
            AccessLevel = string.IsNullOrWhiteSpace(request.AccessLevel)
                ? existing.AccessLevel
                : NormalizeRequired(request.AccessLevel, nameof(request.AccessLevel)),
            Status = string.IsNullOrWhiteSpace(request.Status)
                ? existing.Status
                : NormalizeRequired(request.Status, nameof(request.Status)),
            ExpiresUtc = request.ExpiresUtc,
            Metadata = request.Metadata is null ? existing.Metadata : NormalizeMetadata(request.Metadata),
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        return ResourceAccessGrantInfo.FromRecord(await _store.UpsertGrantAsync(updated, ct).ConfigureAwait(false));
    }

    public async Task RevokeGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateGrantId(grantId);

        var existing = await _store.GetGrantAsync(tenantId, grantId, ct).ConfigureAwait(false);
        if (existing is null)
            return;

        await _store.UpsertGrantAsync(existing with
        {
            Status = ResourceAccessGrantStatuses.Revoked,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);
    }

    internal static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new AccessControlException("tenantId is required.");
    }

    internal static void ValidateGrantId(Guid grantId)
    {
        if (grantId == Guid.Empty)
            throw new AccessControlException("grantId is required.");
    }

    internal static string NormalizeRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AccessControlException($"{name} is required.");

        return value.Trim();
    }

    internal static AccessSubject NormalizeSubject(AccessSubject? subject)
    {
        if (subject is null)
            throw new AccessControlException("subject is required.");

        return new(
            NormalizeRequired(subject.SubjectType, nameof(subject.SubjectType)),
            NormalizeRequired(subject.SubjectId, nameof(subject.SubjectId)));
    }

    internal static bool SubjectMatches(AccessSubject assigned, AccessSubject requested)
        => string.Equals(assigned.SubjectType, requested.SubjectType, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(assigned.SubjectId, requested.SubjectId, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(Dictionary<string, string>? metadata)
        => (metadata ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
