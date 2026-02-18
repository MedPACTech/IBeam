namespace IBeam.Identity.Abstractions.Schema;

public enum IdentitySchemaMode
{
    /// <summary>
    /// Apply migrations automatically.
    /// </summary>
    Apply,

    /// <summary>
    /// Validate schema only. Do not apply changes.
    /// </summary>
    ValidateOnly,

    /// <summary>
    /// Do nothing.
    /// </summary>
    None
}