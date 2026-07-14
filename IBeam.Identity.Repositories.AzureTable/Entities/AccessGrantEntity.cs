using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class AccessGrantEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string TenantId { get; set; } = default!;
    public string GrantId { get; set; } = default!;
    public string SubjectType { get; set; } = default!;
    public string SubjectId { get; set; } = default!;
    public string ResourceType { get; set; } = default!;
    public string ResourceId { get; set; } = default!;
    public string AccessLevel { get; set; } = default!;
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

