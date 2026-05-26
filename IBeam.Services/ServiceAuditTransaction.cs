namespace IBeam.Services.Abstractions;

public sealed class ServiceAuditTransaction
{
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;

    public string ServiceName { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public ServiceAuditOperation Operation { get; set; }

    public Guid? EntityId { get; set; }

    public Guid? TenantId { get; set; }

    public string? ActorId { get; set; }

    public string? CorrelationId { get; set; }

    public string? OriginalJson { get; set; }

    public string? TransformedJson { get; set; }
}

