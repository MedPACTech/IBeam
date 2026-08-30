namespace IBeam.Repositories.Core;

public sealed class RepositoryOptions
{
    public bool EnableCache { get; set; } = true;

    /// <summary>
    /// Safety-net TTL for the select-all cache in BaseRepositoryAsync.GetAllAsync.
    /// Writes through the repository still invalidate the cache immediately; this bounds
    /// staleness when a write path bypasses the repository (bulk import, direct table write,
    /// another process). Null disables expiration (cache until invalidated, the old behavior).
    /// Override per repository type via BaseRepositoryAsync.CacheDuration when different
    /// datasets need different freshness.
    /// </summary>
    public TimeSpan? CacheDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// If true, repository generates an Id when entity.Id is Guid.Empty.
    /// If false, Guid.Empty will throw.
    /// </summary>
    public bool IdGeneratedByRepository { get; set; } = false;

    /// <summary>
    /// If true, the repository hard-deletes and ignores IsDeleted filtering.
    /// </summary>
    public bool DisableSoftDelete { get; set; } = false;
}
