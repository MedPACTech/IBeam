using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

public sealed class TenantInviteEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string InviteId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string DestinationType { get; set; } = string.Empty;
    public string NormalizedDestination { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string InvitedByUserId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? SentUtc { get; set; }
    public DateTimeOffset? RedeemedUtc { get; set; }
    public string? RedeemedByUserId { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string? RevokedByUserId { get; set; }
    public string? RevokedReason { get; set; }
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? RoleIdsCsv { get; set; }
    public string? RoleNamesJson { get; set; }
    public bool SetAsDefaultTenant { get; set; }
    public string? AccessGrantsJson { get; set; }
    public string? RedirectUrl { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string? MetadataJson { get; set; }
    public string? ProfileMetadataJson { get; set; }
    public bool RequirePasswordSetup { get; set; }
}
