namespace IBeam.Services.Abstractions;

public sealed class ServiceAuditTransaction
{
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;

    public string ServiceName { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public ServiceAuditOperation Operation { get; set; }

    public string Action { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public Guid? TenantId { get; set; }

    public string? ActorId { get; set; }

    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? DeviceId { get; set; }

    public string? OriginalJson { get; set; }

    public string? TransformedJson { get; set; }

    public string? BeforeJson
    {
        get => OriginalJson;
        set => OriginalJson = value;
    }

    public string? AfterJson
    {
        get => TransformedJson;
        set => TransformedJson = value;
    }

    public bool Succeeded { get; set; } = true;

    public string? ErrorType { get; set; }

    public string? ErrorMessage { get; set; }

    public long? DurationMs { get; set; }
}
