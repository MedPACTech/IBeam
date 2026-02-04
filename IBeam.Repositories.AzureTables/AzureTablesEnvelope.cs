using Azure;
using Azure.Data.Tables;

namespace IBeam.Repositories.AzureTables.Internal;

public sealed class AzureTableEnvelope : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Data { get; set; } = default!;
    public string? Type { get; set; }
}
