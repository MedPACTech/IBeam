namespace IBeam.Identity.Abstractions.Schema;

public sealed record IdentitySchemaStatus(
    int CurrentVersion,
    int TargetVersion,
    bool IsUpToDate,
    IReadOnlyList<IdentitySchemaStep> PendingSteps);