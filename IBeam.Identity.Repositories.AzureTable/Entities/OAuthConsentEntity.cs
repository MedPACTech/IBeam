using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class OAuthConsentEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ConsentId { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string Resource { get; set; } = default!;
    public string ScopesJson { get; set; } = "[]";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
}
