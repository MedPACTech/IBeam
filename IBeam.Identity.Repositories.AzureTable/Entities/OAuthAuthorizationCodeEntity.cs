using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class OAuthAuthorizationCodeEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string CodeHash { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string RedirectUri { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string ScopesJson { get; set; } = "[]";
    public string Resource { get; set; } = default!;
    public string CodeChallenge { get; set; } = default!;
    public string CodeChallengeMethod { get; set; } = default!;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
}
