using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class AuthAttemptEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Method { get; set; } = default!;
    public string Identifier { get; set; } = default!;
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset? LastFailedAtUtc { get; set; }
    public DateTimeOffset? LastSucceededAtUtc { get; set; }
    public string? LastFailedIp { get; set; }
    public string? LastSucceededIp { get; set; }
    public string? LastUserAgent { get; set; }
    public string? LastDeviceId { get; set; }
    public string? LastCountry { get; set; }
    public string? LastRegion { get; set; }
    public string? LastCity { get; set; }
    public string? LastCorrelationId { get; set; }
    public DateTimeOffset? LastUnlockedAtUtc { get; set; }
    public string? UnlockedByUserId { get; set; }
    public string? UnlockReason { get; set; }
    public string? MetadataJson { get; set; }
}
