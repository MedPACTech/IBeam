using Azure;
using Azure.Data.Tables;

namespace IBeam.AccessControl.Repositories.AzureTable;

internal sealed class PermissionRoleMapEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public Guid TenantId { get; set; }
    public string? PermissionName { get; set; }
    public Guid? PermissionId { get; set; }
    public string RoleNamesCsv { get; set; } = string.Empty;
    public string RoleIdsCsv { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
}
