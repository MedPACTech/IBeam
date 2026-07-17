using IBeam.Repositories.Abstractions;

namespace IBeam.Services.Logging;

public sealed class ServiceAuditLogEntry : IEntity
{
    public Guid Id { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public DateOnly DateUtc { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

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

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public bool IsSelectRollup { get; set; }

    public string? QuerySignature { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public int Count { get; set; }

    public bool Succeeded { get; set; } = true;

    public string? ErrorType { get; set; }

    public string? ErrorMessage { get; set; }

    public long? DurationMs { get; set; }
}
