using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class ApiCredentialEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string CredentialId { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string DisplayName { get; set; } = string.Empty;
    public string? AgentKey { get; set; }
    public string KeyPrefix { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public string RoleNamesCsv { get; set; } = string.Empty;
    public string RoleIdsCsv { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
    public DateTimeOffset? LastUsedUtc { get; set; }
    public string? LastUsedIp { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
    public bool IsDeleted { get; set; }
}
