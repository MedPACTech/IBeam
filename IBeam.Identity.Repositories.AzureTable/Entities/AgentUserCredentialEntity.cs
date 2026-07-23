using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

internal sealed class AgentUserCredentialEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string BindingId { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string AgentUserId { get; set; } = default!;
    public string CredentialId { get; set; } = default!;
    public string? Purpose { get; set; }
    public string? EnvironmentKey { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedUtc { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string? RevokedByUserId { get; set; }
    public string? MetadataJson { get; set; }
}
