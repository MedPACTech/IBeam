namespace IBeam.AccessControl.Services;

public sealed class NoOpResourceAccessHierarchyResolver : IResourceAccessHierarchyResolver
{
    public Task<IReadOnlyList<ResourceAccessResource>> ListAncestorsAsync(
        Guid tenantId,
        string resourceType,
        string resourceId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ResourceAccessResource>>([]);
}
