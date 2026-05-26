using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class ExternalLoginEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = default!;
    public string Provider { get; set; } = default!;
    public string ProviderUserId { get; set; } = default!;
    public string? Email { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}
