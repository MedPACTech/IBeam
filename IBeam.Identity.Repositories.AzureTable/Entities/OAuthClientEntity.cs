using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class OAuthClientEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ClientId { get; set; } = default!;
    public string TenantId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = default!;
    public string ClientType { get; set; } = default!;
    public string RedirectUrisJson { get; set; } = "[]";
    public string AllowedGrantTypesJson { get; set; } = "[]";
    public string AllowedScopesJson { get; set; } = "[]";
    public string AllowedResourcesJson { get; set; } = "[]";
    public bool RequirePkce { get; set; }
    public string Status { get; set; } = default!;
    public string? ClientSecretHash { get; set; }
    public string? ClientSecretHashAlgorithm { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
    public DateTimeOffset? SecretRotatedUtc { get; set; }
    public DateTimeOffset? ClientSecretExpiresUtc { get; set; }
    public DateTimeOffset? DisabledUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
}
