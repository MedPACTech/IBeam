namespace IBeam.Billing.Services;

public sealed class BillingWebhookProcessor : IBillingWebhookProcessor
{
    private readonly IBillingCheckoutGatewayResolver _gateways;
    private readonly IBillingProviderEventService _events;
    private readonly IBillingPurchaseService _purchases;
    private readonly IReadOnlyList<IBillingPaidPurchaseHandler> _paidPurchaseHandlers;

    public BillingWebhookProcessor(
        IBillingCheckoutGatewayResolver gateways,
        IBillingProviderEventService events,
        IBillingPurchaseService purchases,
        IEnumerable<IBillingPaidPurchaseHandler>? paidPurchaseHandlers = null)
    {
        _gateways = gateways;
        _events = events;
        _purchases = purchases;
        _paidPurchaseHandlers = paidPurchaseHandlers?.ToList() ?? [];
    }

    public async Task<BillingWebhookProcessingInfo> ProcessAsync(
        string providerName,
        BillingWebhookRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gateway = _gateways.Resolve(providerName);

        // Verification must finish before any durable Billing state is touched.
        var verified = await gateway.VerifyWebhookAsync(request, ct).ConfigureAwait(false);
        if (!string.Equals(gateway.ProviderName, verified.ProviderName, StringComparison.OrdinalIgnoreCase))
            throw new BillingException("Verified webhook provider does not match the requested provider.");

        var existingEvent = await _events.GetEventAsync(
            verified.ProviderName,
            verified.ProviderEventId,
            ct).ConfigureAwait(false);
        if (existingEvent is not null && IsComplete(existingEvent.Status))
            return FromExisting(existingEvent);

        if (!BillingCommerceEventTypes.TryNormalize(verified.EventType, out var eventType))
            return await RecordIgnoredAsync(verified, null, "unsupported-event", ct).ConfigureAwait(false);

        var purchaseId = ReadPurchaseId(verified.Metadata);
        if (purchaseId is null)
            return await RecordIgnoredAsync(verified, null, "missing-purchase-id", ct, eventType).ConfigureAwait(false);

        var purchase = await _purchases.GetPurchaseAsync(purchaseId.Value, ct).ConfigureAwait(false);
        if (purchase is null)
            return await RecordIgnoredAsync(verified, purchaseId, "purchase-not-found", ct, eventType).ConfigureAwait(false);

        var nextStatus = PurchaseStatusFor(eventType);
        if (nextStatus is not null && verified.OccurredUtc.AddSeconds(1) < purchase.UpdatedUtc)
            return await RecordIgnoredAsync(verified, purchaseId, "stale-event", ct, eventType, purchase.Status).ConfigureAwait(false);

        try
        {
            if (nextStatus is not null)
            {
                purchase = await _purchases.ApplyProviderUpdateAsync(
                    purchase.PurchaseId,
                    new ApplyBillingPurchaseProviderUpdateRequest
                    {
                        ProviderName = verified.ProviderName,
                        ProviderEventId = verified.ProviderEventId,
                        EventType = eventType,
                        Status = nextStatus,
                        OccurredUtc = verified.OccurredUtc,
                        ProviderCheckoutSessionId = verified.ProviderCheckoutSessionId,
                        ProviderCustomerId = verified.ProviderCustomerId,
                        ProviderSubscriptionId = verified.ProviderSubscriptionId,
                        Metadata = new Dictionary<string, string>(verified.Metadata, StringComparer.OrdinalIgnoreCase)
                    },
                    ct).ConfigureAwait(false);

                if (string.Equals(purchase.Status, BillingPurchaseStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var handler in _paidPurchaseHandlers)
                        await handler.HandlePaidPurchaseAsync(purchase, ct).ConfigureAwait(false);
                }
            }

            await RecordAsync(verified, BillingProviderEventStatuses.Processed, purchaseId, eventType, null, ct)
                .ConfigureAwait(false);
            return new BillingWebhookProcessingInfo(
                verified.ProviderName,
                verified.ProviderEventId,
                eventType,
                BillingWebhookProcessingOutcomes.Processed,
                false,
                purchaseId,
                purchase.Status,
                null);
        }
        catch (BillingException)
        {
            return await RecordIgnoredAsync(
                verified,
                purchaseId,
                "invalid-purchase-transition",
                ct,
                eventType,
                purchase.Status).ConfigureAwait(false);
        }
        catch
        {
            await RecordAsync(verified, BillingProviderEventStatuses.Failed, purchaseId, eventType, "processing-failed", ct)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<BillingWebhookProcessingInfo> RecordIgnoredAsync(
        BillingVerifiedWebhookInfo verified,
        Guid? purchaseId,
        string reason,
        CancellationToken ct,
        string? eventType = null,
        string? purchaseStatus = null)
    {
        await RecordAsync(
            verified,
            BillingProviderEventStatuses.Ignored,
            purchaseId,
            eventType ?? verified.EventType,
            reason,
            ct).ConfigureAwait(false);
        return new BillingWebhookProcessingInfo(
            verified.ProviderName,
            verified.ProviderEventId,
            eventType ?? verified.EventType,
            BillingWebhookProcessingOutcomes.Ignored,
            false,
            purchaseId,
            purchaseStatus,
            reason);
    }

    private async Task RecordAsync(
        BillingVerifiedWebhookInfo verified,
        string status,
        Guid? purchaseId,
        string eventType,
        string? reason,
        CancellationToken ct)
    {
        var metadata = new Dictionary<string, string>(verified.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["normalizedEventType"] = eventType
        };
        if (purchaseId is not null)
            metadata["purchaseId"] = purchaseId.Value.ToString("D");
        if (reason is not null)
            metadata["processingReason"] = reason;

        await _events.RecordEventAsync(
            new RecordBillingProviderEventRequest
            {
                ProviderName = verified.ProviderName,
                ProviderEventId = verified.ProviderEventId,
                EventType = eventType,
                Status = status,
                ProviderCustomerId = verified.ProviderCustomerId,
                ProviderSubscriptionId = verified.ProviderSubscriptionId,
                Metadata = metadata
            },
            ct).ConfigureAwait(false);
    }

    private static BillingWebhookProcessingInfo FromExisting(BillingProviderEventInfo existing)
    {
        var ignored = string.Equals(existing.Status, BillingProviderEventStatuses.Ignored, StringComparison.OrdinalIgnoreCase);
        var purchaseId = ReadPurchaseId(existing.Metadata);
        existing.Metadata.TryGetValue("processingReason", out var reason);
        return new BillingWebhookProcessingInfo(
            existing.ProviderName,
            existing.ProviderEventId,
            existing.EventType,
            ignored ? BillingWebhookProcessingOutcomes.Ignored : BillingWebhookProcessingOutcomes.Processed,
            true,
            purchaseId,
            null,
            reason);
    }

    private static bool IsComplete(string status)
        => string.Equals(status, BillingProviderEventStatuses.Processed, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, BillingProviderEventStatuses.Ignored, StringComparison.OrdinalIgnoreCase);

    private static Guid? ReadPurchaseId(IReadOnlyDictionary<string, string> metadata)
        => metadata.TryGetValue("purchaseId", out var value) && Guid.TryParse(value, out var purchaseId) && purchaseId != Guid.Empty
            ? purchaseId
            : null;

    private static string? PurchaseStatusFor(string eventType)
        => eventType switch
        {
            BillingCommerceEventTypes.CheckoutCompleted => BillingPurchaseStatuses.Paid,
            BillingCommerceEventTypes.PaymentSucceeded => BillingPurchaseStatuses.Paid,
            BillingCommerceEventTypes.PaymentFailed => BillingPurchaseStatuses.Failed,
            BillingCommerceEventTypes.SubscriptionCanceled => BillingPurchaseStatuses.Canceled,
            BillingCommerceEventTypes.PaymentRefunded => BillingPurchaseStatuses.Refunded,
            BillingCommerceEventTypes.PaymentDisputed => BillingPurchaseStatuses.Refunded,
            BillingCommerceEventTypes.SubscriptionRenewed => null,
            BillingCommerceEventTypes.SubscriptionSeatsChanged => null,
            _ => null
        };
}
