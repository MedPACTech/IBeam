namespace IBeam.Identity.Schema;

public interface IIdentitySchemaManager
{
    /// <summary>
    /// Returns the current schema state.
    /// </summary>
    Task<IdentitySchemaStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Applies any pending schema updates.
    /// </summary>
    Task ApplyAsync(CancellationToken ct = default);
}