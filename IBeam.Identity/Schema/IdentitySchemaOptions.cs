namespace IBeam.Identity.Schema;

public sealed class IdentitySchemaOptions
{
    /// <summary>
    /// Controls how schema validation and migration are handled.
    /// </summary>
    public IdentitySchemaMode Mode { get; init; } = IdentitySchemaMode.Apply;

    /// <summary>
    /// If true, startup fails when schema is not at the expected version.
    /// </summary>
    public bool FailIfOutOfDate { get; init; } = true;

    /// <summary>
    /// Optional: disable schema handling entirely.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
