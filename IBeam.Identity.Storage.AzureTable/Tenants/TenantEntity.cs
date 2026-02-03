using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Storage.AzureTable.Tenants.Entities;

public sealed class TenantEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "TEN";
    public string RowKey { get; set; } = default!; // tenantId (Guid)

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = "";
    public string OwnerUserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
