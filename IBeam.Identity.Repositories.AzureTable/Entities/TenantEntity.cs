using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class TenantEntity : ITableEntity
{
    public const string TenantsPartitionKey = "TEN";

    // PK = "TEN", RK = tenantId (Guid as string)
    public string PartitionKey { get; set; } = TenantsPartitionKey;
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = "";
    public string? NormalizedName { get; set; }        // e.g. Name.Trim().ToUpperInvariant()

    public string? OwnerUserId { get; set; }           // optional
    public string Status { get; set; } = "Active";     // "Active" | "Disabled"

    public DateTimeOffset CreatedAt { get; set; }      // set explicitly on insert
    public DateTimeOffset? UpdatedAt { get; set; }
}
