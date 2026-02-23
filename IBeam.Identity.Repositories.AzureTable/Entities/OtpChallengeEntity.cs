using Azure;
using Azure.Data.Tables;

internal sealed class OtpChallengeEntity : ITableEntity
{
    // Keys (computed by provider key helper)
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Identity / scoping
    public string ChallengeId { get; set; } = default!;      // Guid as string (no leaks)
    public string? TenantId { get; set; }                    // null allowed for pre-tenant flows
    public string Purpose { get; set; } = default!;          // e.g. "login", "mfa", "tenant-select", etc.

    // Target
    public string Channel { get; set; } = default!;          // "sms" | "email"
    public string Destination { get; set; } = default!;      // phone/email normalized
    public string DestinationHash { get; set; } = default!;  // stable hash for partitioning / lookups (optional but useful)

    // OTP secret (never store raw code)
    public string CodeHash { get; set; } = default!;         // hash(code + nonce/salt)
    public string CodeNonce { get; set; } = default!;        // random per challenge (or salt)

    // Lifecycle
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsConsumed { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }

    // Security / abuse controls
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? ResendAvailableAt { get; set; }

    public string? VerificationToken { get; set; }
    public DateTimeOffset? VerificationTokenExpiresAt { get; set; }

}
