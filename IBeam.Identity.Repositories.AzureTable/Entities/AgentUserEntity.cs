using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class AgentUserEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string AgentUserId { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgentType { get; set; } = "custom";
    public string AgentKey { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedUtc { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
    public string? MetadataJson { get; set; }
}
