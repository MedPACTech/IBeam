using Azure;
using Azure.Data.Tables;

namespace IBeam.AccessControl.Repositories.AzureTable;

internal sealed class ResourceAccessGrantEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid GrantId { get; set; }
    public Guid TenantId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string AccessLevel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
    public string MetadataJson { get; set; } = string.Empty;
}
