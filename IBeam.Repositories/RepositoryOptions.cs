namespace IBeam.Repositories.Core;

public sealed class RepositoryOptions
{
    public bool EnableCache { get; set; } = true;

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
