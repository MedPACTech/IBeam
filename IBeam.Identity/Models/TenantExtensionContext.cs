namespace IBeam.Identity.Models;

public sealed record TenantExtensionContext(
    string Operation,
    Guid? AuthUserId = null,
    string? CorrelationId = null,
    string? CausationId = null,
    string? TraceId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>Privacy mode for external administration; this flag never confers Identity authority.</summary>
    public bool RedactFailureDetails { get; init; }
    /// <summary>Per-operation delivery requirement; normal Identity calls keep the host's existing failure policy.</summary>
    public bool RequireLifecycleDelivery { get; init; }
    public static TenantExtensionContext Create(
        string operation,
        Guid? authUserId = null,
        string? correlationId = null,
        string? causationId = null,
        string? traceId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new(
            string.IsNullOrWhiteSpace(operation) ? TenantExtensionOperations.Ensure : operation.Trim(),
            authUserId,
            correlationId,
            causationId,
            traceId,
            metadata);
}

public static class TenantExtensionOperations
{
    public const string Ensure = "ensure";
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Activated = "activated";
    public const string Deactivated = "deactivated";
    public const string Selected = "selected";
    public const string Listed = "listed";
    public const string Linked = "linked";
}
