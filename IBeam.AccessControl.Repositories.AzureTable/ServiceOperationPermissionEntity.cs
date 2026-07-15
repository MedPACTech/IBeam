using Azure;
using Azure.Data.Tables;

namespace IBeam.AccessControl.Repositories.AzureTable;

internal sealed class ServiceOperationPermissionEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid RuleId { get; set; }
    public Guid TenantId { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public string Effect { get; set; } = string.Empty;
    public string SubjectTypesCsv { get; set; } = string.Empty;
    public string RoleNamesCsv { get; set; } = string.Empty;
    public string RoleIdsCsv { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

