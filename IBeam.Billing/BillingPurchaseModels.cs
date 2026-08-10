namespace IBeam.Billing;

public sealed record BillingPurchaseRecord(
    Guid PurchaseId,
    Guid CorrelationId,
    Guid? TenantId,
    Guid? UserId,
    Guid? LicenseKey,
    string? BuyerEmail,
    string OfferKey,
    string ProductKey,
    string PlanKey,
    int TotalSeats,
    string Currency,
    decimal AmountSubtotal,
    decimal AmountTax,
    decimal AmountTotal,
    string Status,
    string? ProviderName,
    string? ProviderCheckoutSessionId,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    string? LastProviderEventId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? PaidUtc,
    DateTimeOffset? FulfilledUtc,
    DateTimeOffset? ClaimedUtc,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? CanceledUtc,
    DateTimeOffset? RefundedUtc,
    DateTimeOffset? FailedUtc,
    DateTimeOffset? BuyerEmailRedactedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public BillingPurchaseInfo ToInfo() => BillingPurchaseInfo.FromRecord(this);
}

public sealed record BillingPurchaseInfo(
    Guid PurchaseId,
    Guid CorrelationId,
    Guid? TenantId,
    Guid? UserId,
    Guid? LicenseKey,
    string? BuyerEmail,
    string OfferKey,
    string ProductKey,
    string PlanKey,
    int TotalSeats,
    string Currency,
    decimal AmountSubtotal,
    decimal AmountTax,
    decimal AmountTotal,
    string Status,
    string? ProviderName,
    string? ProviderCheckoutSessionId,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    string? LastProviderEventId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? PaidUtc,
    DateTimeOffset? FulfilledUtc,
    DateTimeOffset? ClaimedUtc,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? CanceledUtc,
    DateTimeOffset? RefundedUtc,
    DateTimeOffset? FailedUtc,
    DateTimeOffset? BuyerEmailRedactedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingPurchaseInfo FromRecord(BillingPurchaseRecord record)
        => new(
            record.PurchaseId,
            record.CorrelationId,
            NormalizeOptionalGuid(record.TenantId),
            NormalizeOptionalGuid(record.UserId),
            NormalizeOptionalGuid(record.LicenseKey),
            NormalizeEmail(record.BuyerEmail),
            BillingPriceReferenceInfo.NormalizeRequired(record.OfferKey, nameof(record.OfferKey)),
            BillingPriceReferenceInfo.NormalizeRequired(record.ProductKey, nameof(record.ProductKey)),
            BillingPriceReferenceInfo.NormalizeRequired(record.PlanKey, nameof(record.PlanKey)),
            record.TotalSeats,
            BillingPriceReferenceInfo.NormalizeCurrency(record.Currency) ?? string.Empty,
            record.AmountSubtotal,
            record.AmountTax,
            record.AmountTotal,
            BillingPurchaseStatuses.Normalize(record.Status),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderName)?.ToLowerInvariant(),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderCheckoutSessionId),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderCustomerId),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderSubscriptionId),
            BillingPriceReferenceInfo.NormalizeOptional(record.LastProviderEventId),
            record.CreatedUtc,
            record.UpdatedUtc,
            record.PaidUtc,
            record.FulfilledUtc,
            record.ClaimedUtc,
            record.ExpiresUtc,
            record.CanceledUtc,
            record.RefundedUtc,
            record.FailedUtc,
            record.BuyerEmailRedactedUtc,
            BillingPriceReferenceInfo.NormalizeMetadata(record.Metadata));

    public static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static Guid? NormalizeOptionalGuid(Guid? value)
        => value == Guid.Empty ? null : value;
}

public sealed class CreatePendingBillingPurchaseRequest
{
    public Guid CorrelationId { get; set; }
    public string BuyerEmail { get; set; } = string.Empty;
    public string OfferKey { get; set; } = string.Empty;
    public string ProductKey { get; set; } = string.Empty;
    public string PlanKey { get; set; } = string.Empty;
    public int TotalSeats { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AmountSubtotal { get; set; }
    public decimal AmountTax { get; set; }
    public decimal AmountTotal { get; set; }
    public string? ProviderName { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class ApplyBillingPurchaseProviderUpdateRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? OccurredUtc { get; set; }
    public string? ProviderCheckoutSessionId { get; set; }
    public string? ProviderCustomerId { get; set; }
    public string? ProviderSubscriptionId { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public static class BillingPurchaseStatuses
{
    public const string Initiated = "initiated";
    public const string AwaitingPayment = "awaiting-payment";
    public const string Paid = "paid";
    public const string Fulfilled = "fulfilled";
    public const string Claimed = "claimed";
    public const string Expired = "expired";
    public const string Canceled = "canceled";
    public const string Refunded = "refunded";
    public const string Failed = "failed";

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedTransitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Initiated] = new HashSet<string>([AwaitingPayment, Paid, Expired, Canceled, Failed], StringComparer.OrdinalIgnoreCase),
            [AwaitingPayment] = new HashSet<string>([Paid, Expired, Canceled, Failed], StringComparer.OrdinalIgnoreCase),
            [Paid] = new HashSet<string>([Fulfilled, Refunded], StringComparer.OrdinalIgnoreCase),
            [Fulfilled] = new HashSet<string>([Claimed, Refunded], StringComparer.OrdinalIgnoreCase),
            [Claimed] = new HashSet<string>([Refunded], StringComparer.OrdinalIgnoreCase),
            [Expired] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [Canceled] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [Refunded] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [Failed] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

    public static string Normalize(string value)
        => BillingPriceReferenceInfo.NormalizeRequired(value, nameof(value))
            .Trim()
            .Replace('_', '-')
            .ToLowerInvariant() switch
        {
            Initiated => Initiated,
            AwaitingPayment => AwaitingPayment,
            Paid => Paid,
            Fulfilled => Fulfilled,
            Claimed => Claimed,
            Expired => Expired,
            Canceled => Canceled,
            Refunded => Refunded,
            Failed => Failed,
            _ => throw new ArgumentException($"Purchase status '{value}' is not supported.", nameof(value))
        };

    public static void RequireTransition(string currentStatus, string nextStatus)
    {
        var current = Normalize(currentStatus);
        var next = Normalize(nextStatus);
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
            return;
        if (!AllowedTransitions[current].Contains(next))
            throw new BillingException($"Purchase status cannot transition from '{current}' to '{next}'.");
    }
}
