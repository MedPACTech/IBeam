namespace IBeam.Identity.Abstractions.Schema;

public sealed record IdentitySchemaStep(
    int Version,
    string Description);