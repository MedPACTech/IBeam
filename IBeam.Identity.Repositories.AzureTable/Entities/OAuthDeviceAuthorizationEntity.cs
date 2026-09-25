using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

// The primary row, keyed by a hash of the device_code (the secret only the polling device holds).
internal sealed class OAuthDeviceAuthorizationEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string DeviceCodeHash { get; set; } = default!;
    public string UserCode { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string ScopesJson { get; set; } = "[]";
    public string Resource { get; set; } = default!;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public int IntervalSeconds { get; set; }
    public string? UserId { get; set; }
    public string? TenantId { get; set; }
    public string? GrantedScopesJson { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public DateTimeOffset? DeniedUtc { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
    public DateTimeOffset? LastPolledUtc { get; set; }
}

// A pointer row keyed by the user_code (the short, human-typed secret the approving browser
// holds), so the approval endpoint doesn't need to scan the DeviceCodeHash-keyed partition space
// to find a record it only knows by its other key.
internal sealed class OAuthDeviceUserCodeIndexEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string DeviceCodeHash { get; set; } = default!;
}
