using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Tenants;

// UserTenants table entity: PK = "USR#{userId}", RK = "TEN#{tenantId}"
public sealed class UserTenantEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Status { get; set; } = "Active";
    public string RolesCsv { get; set; } = "";
    public string? DisplayName { get; set; } // optional

    public bool IsDefault { get; set; } // default/current tenant
    public DateTimeOffset? LastSelectedAt { get; set; }
}
