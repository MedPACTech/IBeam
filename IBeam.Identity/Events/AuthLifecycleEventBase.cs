namespace IBeam.Identity.Events;

public abstract record AuthLifecycleEventBase
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public string EventType { get; init; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? TraceId { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
