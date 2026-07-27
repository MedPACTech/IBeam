namespace IBeam.Billing;

public sealed record BillingSubscriptionRecord(
    Guid BillingSubscriptionId,
    Guid TenantId,
    Guid? UserId,
    Guid BillingCustomerId,
    string? ProductKey,
    string? PlanKey,
    string BillingMode,
    string Status,
    int? SeatQuantity,
    BillingPriceReferenceInfo? Price,
    string? ProviderName,
    string? ProviderSubscriptionId,
    string? ProviderStatus,
    DateTimeOffset? CurrentPeriodStartsUtc,
    DateTimeOffset? CurrentPeriodEndsUtc,
    bool CancelAtPeriodEnd,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public BillingSubscriptionInfo ToInfo()
        => BillingSubscriptionInfo.FromRecord(this);
}

public sealed record BillingSubscriptionInfo(
    Guid BillingSubscriptionId,
    Guid TenantId,
    Guid? UserId,
    Guid BillingCustomerId,
    string? ProductKey,
    string? PlanKey,
    string BillingMode,
    string Status,
    int? SeatQuantity,
    BillingPriceReferenceInfo? Price,
    string? ProviderName,
    string? ProviderSubscriptionId,
    string? ProviderStatus,
    DateTimeOffset? CurrentPeriodStartsUtc,
    DateTimeOffset? CurrentPeriodEndsUtc,
    bool CancelAtPeriodEnd,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingSubscriptionInfo FromRecord(BillingSubscriptionRecord record)
        => new(
            record.BillingSubscriptionId,
            record.TenantId,
            record.UserId,
            record.BillingCustomerId,
            BillingPriceReferenceInfo.NormalizeOptional(record.ProductKey),
            BillingPriceReferenceInfo.NormalizeOptional(record.PlanKey),
            BillingModes.Normalize(record.BillingMode),
            BillingSubscriptionStatuses.Normalize(record.Status),
            record.SeatQuantity,
            record.Price,
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderName),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderSubscriptionId),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderStatus),
            record.CurrentPeriodStartsUtc,
            record.CurrentPeriodEndsUtc,
            record.CancelAtPeriodEnd,
            record.CreatedUtc,
            record.UpdatedUtc,
            BillingPriceReferenceInfo.NormalizeMetadata(record.Metadata));
}

public sealed class CreateBillingSubscriptionRequest
{
    public Guid BillingCustomerId { get; set; }
    public Guid? UserId { get; set; }
    public string? ProductKey { get; set; }
    public string? PlanKey { get; set; }
    public string? BillingMode { get; set; }
    public string? Status { get; set; }
    public int? SeatQuantity { get; set; }
    public BillingPriceReferenceInfo? Price { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderSubscriptionId { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? CurrentPeriodStartsUtc { get; set; }
    public DateTimeOffset? CurrentPeriodEndsUtc { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class UpdateBillingSubscriptionRequest
{
    public string? ProductKey { get; set; }
    public string? PlanKey { get; set; }
    public string? BillingMode { get; set; }
    public string? Status { get; set; }
    public int? SeatQuantity { get; set; }
    public BillingPriceReferenceInfo? Price { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderSubscriptionId { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? CurrentPeriodStartsUtc { get; set; }
    public DateTimeOffset? CurrentPeriodEndsUtc { get; set; }
    public bool? CancelAtPeriodEnd { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
