using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Tenants;

// TenantUsers table entity: PK = "TEN#{tenantId}", RK = "USR#{userId}"
public sealed class TenantUserEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Status { get; set; } = "Active";
    public string RolesCsv { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
