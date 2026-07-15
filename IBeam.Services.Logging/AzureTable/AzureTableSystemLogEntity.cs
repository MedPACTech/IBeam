using Azure;
using Azure.Data.Tables;

namespace IBeam.Services.Logging.AzureTable;

internal sealed class AzureTableSystemLogEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
    public string DateUtc { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }

    public string? ServiceName { get; set; }
    public string? EntityName { get; set; }
    public string? Operation { get; set; }
    public string? Action { get; set; }
    public Guid? EntityId { get; set; }
    public Guid? TenantId { get; set; }
    public string? ActorId { get; set; }

    public string? TraceId { get; set; }
    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceId { get; set; }

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? OriginalJson { get; set; }
    public string? TransformedJson { get; set; }

    public bool IsSelectRollup { get; set; }
    public string? QuerySignature { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public int Count { get; set; }
}
