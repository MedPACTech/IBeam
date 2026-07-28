namespace IBeam.Credits;

public sealed record CreditReservationInfo(
    Guid CreditReservationId,
    Guid TenantId,
    Guid CreditAccountId,
    string BucketKey,
    decimal EstimatedAmount,
    decimal MaxAmount,
    decimal ReservedAmount,
    decimal? ActualAmount,
    string Status,
    string? OperationName,
    string? IdempotencyKey,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? SettledUtc,
    DateTimeOffset? ReleasedUtc,
    DateTimeOffset? ExpiredUtc,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class ReserveCreditsRequest
{
    public Guid CreditAccountId { get; set; }
    public string BucketKey { get; set; } = string.Empty;
    public decimal EstimatedAmount { get; set; }
    public decimal MaxAmount { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
    public string? OperationName { get; set; }
    public string? IdempotencyKey { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class SettleCreditReservationRequest
{
    public decimal ActualAmount { get; set; }
    public string? OperationName { get; set; }
    public string? IdempotencyKey { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class ReleaseCreditReservationRequest
{
    public string? Reason { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class RecordCreditUsageRequest
{
    public Guid CreditAccountId { get; set; }
    public string BucketKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? OperationName { get; set; }
    public string? IdempotencyKey { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed record CreditUsageRecordResult(
    CreditLedgerEntryInfo LedgerEntry,
    CreditBalanceInfo Balance);

public static class CreditReservationStatuses
{
    public const string Active = "active";
    public const string Settled = "settled";
    public const string Released = "released";
    public const string Expired = "expired";

    public static string Normalize(string? status)
        => CreditNormalization.NormalizeKnown(status, Active);
}
