using Azure;
using Azure.Data.Tables;

namespace IBeam.Commerce.Repositories.AzureTable;

public sealed class AzureTableJsonEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string? EntityId { get; set; }
    public string? BucketKey { get; set; }
    public string? Status { get; set; }
    public string? IdempotencyKey { get; set; }
}
