using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class AccessCatalogOverrideEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string TenantId { get; set; } = default!;
    public string CatalogItemId { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string? Description { get; set; }
    public string Category { get; set; } = default!;
    public bool IsAssignable { get; set; } = true;
    public bool IsMutable { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public string? SubjectTypesCsv { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? ParentResourceType { get; set; }
    public string? ParentResourceId { get; set; }
    public string? SupportedAccessLevelsCsv { get; set; }
    public int? Rank { get; set; }
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
