namespace IBeam.Credits;

public sealed class GetCreditRuntimeSummaryRequest
{
    public Guid CreditAccountId { get; set; }
    public List<string> BucketKeys { get; set; } = [];
    public DateTimeOffset? AsOfUtc { get; set; }
}

public sealed class ListCreditLedgerEntriesRequest
{
    public Guid CreditAccountId { get; set; }
    public string? BucketKey { get; set; }
}

public sealed class ListCreditReservationsRequest
{
    public Guid? CreditAccountId { get; set; }
    public string? BucketKey { get; set; }
}

public sealed record CreditRuntimeSummaryInfo(
    Guid TenantId,
    Guid CreditAccountId,
    bool GuidanceOnly,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<CreditBucketBalanceSummaryInfo> Balances);

public sealed record CreditBucketBalanceSummaryInfo(
    string BucketKey,
    decimal Granted,
    decimal Debited,
    decimal Expired,
    decimal LedgerAvailable,
    decimal ActiveReserved,
    decimal AvailableAfterReservations,
    DateTimeOffset AsOfUtc);
