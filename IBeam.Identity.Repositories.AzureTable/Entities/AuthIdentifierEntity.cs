using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class AuthIdentifierEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = default!;
    public string IdentifierType { get; set; } = default!;
    public string Identifier { get; set; } = default!;
    public DateTimeOffset BoundAtUtc { get; set; }
}
