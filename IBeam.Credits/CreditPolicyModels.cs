namespace IBeam.Credits;

public sealed class BeginCreditOperationRequest
{
    public Guid CreditAccountId { get; set; }
    public string BucketKey { get; set; } = string.Empty;
    public string PolicyMode { get; set; } = CreditPolicyModes.StrictPrepaid;
    public decimal EstimatedAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public decimal? MaxCredits { get; set; }
    public DateTimeOffset? ReservationExpiresUtc { get; set; }
    public string? OperationName { get; set; }
    public string? IdempotencyKey { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class CompleteCreditOperationRequest
{
    public Guid CreditAccountId { get; set; }
    public string BucketKey { get; set; } = string.Empty;
    public string PolicyMode { get; set; } = CreditPolicyModes.StrictPrepaid;
    public Guid? CreditReservationId { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal? MaxCredits { get; set; }
    public bool AllowOverage { get; set; }
    public string? OperationName { get; set; }
    public string? IdempotencyKey { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class RecordStreamingCreditChunkRequest
{
    public Guid CreditAccountId { get; set; }
    public string BucketKey { get; set; } = string.Empty;
    public decimal ChunkAmount { get; set; }
    public decimal ConsumedToDate { get; set; }
    public decimal? MaxCredits { get; set; }
    public bool AllowOverage { get; set; }
    public string? OperationName { get; set; }
    public string? IdempotencyKey { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed record CreditOperationDecision(
    bool Approved,
    string PolicyMode,
    string? DenialReason,
    string? Message,
    decimal EstimatedAmount,
    decimal? MaxAmount,
    decimal? MaxCredits,
    CreditReservationInfo? Reservation)
{
    public static CreditOperationDecision ApprovedWith(
        string policyMode,
        decimal estimatedAmount,
        decimal? maxAmount,
        decimal? maxCredits,
        CreditReservationInfo? reservation = null)
        => new(true, policyMode, null, null, estimatedAmount, maxAmount, maxCredits, reservation);

    public static CreditOperationDecision Denied(
        string policyMode,
        string denialReason,
        string message,
        decimal estimatedAmount,
        decimal? maxAmount = null,
        decimal? maxCredits = null)
        => new(false, policyMode, denialReason, message, estimatedAmount, maxAmount, maxCredits, null);
}

public sealed record CreditOperationSettlementResult(
    bool Approved,
    string PolicyMode,
    string? DenialReason,
    string? Message,
    decimal ActualAmount,
    decimal SettledAmount,
    decimal OverageAmount,
    CreditReservationInfo? Reservation,
    CreditLedgerEntryInfo? LedgerEntry,
    CreditBalanceInfo? Balance)
{
    public static CreditOperationSettlementResult ApprovedWith(
        string policyMode,
        decimal actualAmount,
        decimal settledAmount,
        decimal overageAmount,
        CreditBalanceInfo balance,
        CreditReservationInfo? reservation = null,
        CreditLedgerEntryInfo? ledgerEntry = null)
        => new(true, policyMode, null, null, actualAmount, settledAmount, overageAmount, reservation, ledgerEntry, balance);

    public static CreditOperationSettlementResult Denied(
        string policyMode,
        string denialReason,
        string message,
        decimal actualAmount)
        => new(false, policyMode, denialReason, message, actualAmount, 0, 0, null, null, null);
}

public static class CreditPolicyModes
{
    public const string StrictPrepaid = "strict-prepaid";
    public const string SoftOverage = "soft-overage";
    public const string FailOpenMetering = "fail-open-metering";
    public const string CapByRequest = "cap-by-request";
    public const string Streaming = "streaming";

    public static string Normalize(string? mode)
        => CreditNormalization.NormalizeKnown(mode, StrictPrepaid);
}

public static class CreditPolicyDenialReasons
{
    public const string InsufficientCredits = "insufficient-credits";
    public const string MaxCreditsRequired = "max-credits-required";
    public const string MaxCreditsExceeded = "max-credits-exceeded";
}
