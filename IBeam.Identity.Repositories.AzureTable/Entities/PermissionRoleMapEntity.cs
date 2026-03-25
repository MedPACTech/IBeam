using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class PermissionRoleMapEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string TenantId { get; set; } = default!;
    public string? PermissionName { get; set; }
    public string? PermissionId { get; set; }
    public string RoleNamesCsv { get; set; } = string.Empty;
    public string RoleIdsCsv { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTimeOffset UpdatedAt { get; set; }
}
