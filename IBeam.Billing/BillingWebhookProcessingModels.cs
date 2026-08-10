namespace IBeam.Billing;

public interface IBillingWebhookProcessor
{
    Task<BillingWebhookProcessingInfo> ProcessAsync(
        string providerName,
        BillingWebhookRequest request,
        CancellationToken ct = default);
}

public sealed record BillingWebhookProcessingInfo(
    string ProviderName,
    string ProviderEventId,
    string EventType,
    string Outcome,
    bool IsReplay,
    Guid? PurchaseId,
    string? PurchaseStatus,
    string? Reason);

public static class BillingWebhookProcessingOutcomes
{
    public const string Processed = "processed";
    public const string Ignored = "ignored";
}

public static class BillingCommerceEventTypes
{
    public const string CheckoutCompleted = "checkout.completed";
    public const string PaymentSucceeded = "payment.succeeded";
    public const string PaymentFailed = "payment.failed";
    public const string SubscriptionRenewed = "subscription.renewed";
    public const string SubscriptionSeatsChanged = "subscription.seats-changed";
    public const string SubscriptionCanceled = "subscription.canceled";
    public const string PaymentRefunded = "payment.refunded";
    public const string PaymentDisputed = "payment.disputed";

    private static readonly IReadOnlySet<string> Supported = new HashSet<string>(
        [
            CheckoutCompleted,
            PaymentSucceeded,
            PaymentFailed,
            SubscriptionRenewed,
            SubscriptionSeatsChanged,
            SubscriptionCanceled,
            PaymentRefunded,
            PaymentDisputed
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool TryNormalize(string? value, out string eventType)
    {
        var candidate = value?.Trim().Replace('_', '-').ToLowerInvariant();
        if (candidate is not null && Supported.Contains(candidate))
        {
            eventType = candidate;
            return true;
        }

        eventType = string.Empty;
        return false;
    }

    public static string Normalize(string value)
        => TryNormalize(value, out var eventType)
            ? eventType
            : throw new ArgumentException($"Commerce event type '{value}' is not supported.", nameof(value));
}
