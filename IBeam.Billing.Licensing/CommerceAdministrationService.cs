using IBeam.Licensing;
using IBeam.Services.Abstractions;

namespace IBeam.Billing.Licensing;

[IBeamOperation("billing.commerce-admin", PermissionName = "billing.commerce.admin")]
public sealed class CommerceAdministrationService : ICommerceAdministrationService
{
    private readonly IBillingPurchaseService _purchases;
    private readonly IBillingPurchaseClaimService _claims;
    private readonly IBillingPurchaseClaimStore _claimStore;
    private readonly IBillingSubscriptionService _subscriptions;
    private readonly IBillingSubscriptionProviderBindingStore _bindings;
    private readonly IBillingProviderEventService _events;
    private readonly IBillingStore _billingStore;
    private readonly ITenantLicenseService _licenses;
    private readonly ILicenseSeatAssignmentService _assignments;
    private readonly IReadOnlyList<IBillingPaidPurchaseHandler> _paidHandlers;
    private readonly IServiceOperationExecutor _operations;

    public CommerceAdministrationService(
        IBillingPurchaseService purchases,
        IBillingPurchaseClaimService claims,
        IBillingPurchaseClaimStore claimStore,
        IBillingSubscriptionService subscriptions,
        IBillingSubscriptionProviderBindingStore bindings,
        IBillingProviderEventService events,
        IBillingStore billingStore,
        ITenantLicenseService licenses,
        ILicenseSeatAssignmentService assignments,
        IEnumerable<IBillingPaidPurchaseHandler>? paidHandlers = null,
        IServiceOperationExecutor? operations = null)
    {
        _purchases = purchases;
        _claims = claims;
        _claimStore = claimStore;
        _subscriptions = subscriptions;
        _bindings = bindings;
        _events = events;
        _billingStore = billingStore;
        _licenses = licenses;
        _assignments = assignments;
        _paidHandlers = paidHandlers?.ToList() ?? [];
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("billing.commerce-admin.inspect")]
    public Task<CommerceAdministrationSnapshot> InspectAsync(Guid tenantId, Guid purchaseId, CancellationToken ct = default)
        => ExecuteAsync(tenantId, purchaseId, token => InspectCoreAsync(tenantId, purchaseId, token), ct);

    [IBeamOperation("billing.commerce-admin.retry-event", AuditAction = "recovery")]
    public Task<BillingWebhookProcessingInfo> RetryVerifiedEventAsync(
        Guid tenantId,
        string providerName,
        string providerEventId,
        CommerceRecoveryRequest request,
        CancellationToken ct = default)
        => ExecuteAsync(tenantId, null, token => RetryEventCoreAsync(tenantId, providerName, providerEventId, request, token), ct);

    [IBeamOperation("billing.commerce-admin.retry-fulfillment", AuditAction = "recovery")]
    public Task<BillingPurchaseInfo> RetryFulfillmentAsync(
        Guid tenantId,
        Guid purchaseId,
        CommerceRecoveryRequest request,
        CancellationToken ct = default)
        => ExecuteAsync(tenantId, purchaseId, token => RetryFulfillmentCoreAsync(tenantId, purchaseId, request, token), ct);

    [IBeamOperation("billing.commerce-admin.resend-claim", AuditAction = "credential-rotation")]
    public Task<IssuedBillingPurchaseClaimInfo> ResendClaimAsync(
        Guid tenantId,
        Guid purchaseId,
        CommerceRecoveryRequest request,
        CancellationToken ct = default)
        => ExecuteAsync(tenantId, purchaseId, async token =>
        {
            RequireReason(request);
            await RequirePurchaseAsync(tenantId, purchaseId, token).ConfigureAwait(false);
            return await _claims.IssueAsync(purchaseId, token).ConfigureAwait(false);
        }, ct);

    [IBeamOperation("billing.commerce-admin.manual-correction", AuditAction = "manual-correction")]
    public Task<BillingPurchaseInfo> ApplyManualCorrectionAsync(
        Guid tenantId,
        Guid purchaseId,
        ManualCommerceCorrectionRequest request,
        CancellationToken ct = default)
        => ExecuteAsync(tenantId, purchaseId, token => ApplyCorrectionCoreAsync(tenantId, purchaseId, request, token), ct);

    private async Task<CommerceAdministrationSnapshot> InspectCoreAsync(Guid tenantId, Guid purchaseId, CancellationToken ct)
    {
        var purchase = await RequirePurchaseAsync(tenantId, purchaseId, ct).ConfigureAwait(false);
        var claim = await _claimStore.GetByPurchaseAsync(purchaseId, ct).ConfigureAwait(false);
        TenantLicenseInfo? license = null;
        IReadOnlyList<LicenseSeatAssignmentInfo> assignments = [];
        if (purchase.LicenseKey is { } licenseKey)
        {
            license = await _licenses.GetLicenseByKeyAsync(tenantId, licenseKey, ct).ConfigureAwait(false);
            if (license is not null)
                assignments = await _assignments.ListAssignmentsAsync(tenantId, license.LicenseId, ct).ConfigureAwait(false);
        }

        BillingSubscriptionInfo? subscription = null;
        IReadOnlyList<BillingSubscriptionProviderBindingInfo> bindings = [];
        if (!string.IsNullOrWhiteSpace(purchase.ProviderSubscriptionId))
        {
            subscription = (await _subscriptions.ListSubscriptionsAsync(tenantId, ct).ConfigureAwait(false))
                .FirstOrDefault(x => string.Equals(x.ProviderSubscriptionId, purchase.ProviderSubscriptionId, StringComparison.OrdinalIgnoreCase));
            if (subscription is not null)
                bindings = await _bindings.ListBindingsAsync(tenantId, subscription.BillingSubscriptionId, ct).ConfigureAwait(false);
        }

        return new CommerceAdministrationSnapshot(
            ToSummary(purchase),
            subscription,
            license,
            assignments,
            claim is null ? null : new CommerceClaimSummary(
                claim.ClaimId, claim.PurchaseId, claim.LicenseKey, claim.IssuedUtc, claim.ExpiresUtc,
                claim.ClaimedTenantId, claim.ClaimedUserId, claim.ClaimedUtc),
            bindings);
    }

    private async Task<BillingWebhookProcessingInfo> RetryEventCoreAsync(
        Guid tenantId,
        string providerName,
        string providerEventId,
        CommerceRecoveryRequest request,
        CancellationToken ct)
    {
        RequireReason(request);
        var providerEvent = await _events.GetEventAsync(providerName, providerEventId, ct).ConfigureAwait(false)
            ?? throw new BillingException("Provider event was not found.");
        if (providerEvent.TenantId != tenantId)
            throw new BillingException("Provider event does not belong to the requested tenant.");
        if (!string.Equals(providerEvent.Status, BillingProviderEventStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            throw new BillingException("Only failed verified provider events can be retried.");
        if (!providerEvent.Metadata.TryGetValue("purchaseId", out var rawPurchaseId) || !Guid.TryParse(rawPurchaseId, out var purchaseId))
            throw new BillingException("Provider event does not identify a purchase.");

        var purchase = await RequirePurchaseAsync(tenantId, purchaseId, ct).ConfigureAwait(false);
        var nextStatus = PurchaseStatusFor(providerEvent.EventType);
        if (nextStatus is not null)
        {
            purchase = await _purchases.ApplyProviderUpdateAsync(
                purchaseId,
                new ApplyBillingPurchaseProviderUpdateRequest
                {
                    ProviderName = providerEvent.ProviderName,
                    ProviderEventId = providerEvent.ProviderEventId,
                    EventType = providerEvent.EventType,
                    Status = nextStatus,
                    ProviderCustomerId = providerEvent.ProviderCustomerId,
                    ProviderSubscriptionId = providerEvent.ProviderSubscriptionId,
                    Metadata = new Dictionary<string, string> { ["recoveryReason"] = RequireReason(request) }
                },
                ct).ConfigureAwait(false);
        }
        if (string.Equals(purchase.Status, BillingPurchaseStatuses.Paid, StringComparison.OrdinalIgnoreCase))
        {
            await RunPaidHandlersAsync(purchase, ct).ConfigureAwait(false);
            purchase = await _purchases.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false)
                       ?? throw new BillingException("Purchase was not found after recovery.");
        }

        var record = new BillingProviderEventRecord(
            providerEvent.BillingProviderEventId, providerEvent.ProviderName, providerEvent.ProviderEventId,
            providerEvent.EventType, BillingProviderEventStatuses.Processed, providerEvent.ReceivedUtc,
            DateTimeOffset.UtcNow, providerEvent.TenantId, providerEvent.UserId, providerEvent.ProviderCustomerId,
            providerEvent.ProviderSubscriptionId, providerEvent.ProviderInvoiceId, null, null,
            MergeMetadata(providerEvent.Metadata, "recoveryReason", RequireReason(request)));
        await _billingStore.SaveProviderEventAsync(record, ct).ConfigureAwait(false);
        return new BillingWebhookProcessingInfo(
            providerEvent.ProviderName, providerEvent.ProviderEventId, providerEvent.EventType,
            BillingWebhookProcessingOutcomes.Processed, false, purchaseId, purchase.Status, null);
    }

    private async Task<BillingPurchaseInfo> RetryFulfillmentCoreAsync(
        Guid tenantId,
        Guid purchaseId,
        CommerceRecoveryRequest request,
        CancellationToken ct)
    {
        RequireReason(request);
        var purchase = await RequirePurchaseAsync(tenantId, purchaseId, ct).ConfigureAwait(false);
        if (purchase.Status is not (BillingPurchaseStatuses.Paid or BillingPurchaseStatuses.Fulfilled))
            throw new BillingException("Only paid or fulfilled purchases can retry fulfillment.");
        await RunPaidHandlersAsync(purchase, ct).ConfigureAwait(false);
        return await _purchases.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false)
               ?? throw new BillingException("Purchase was not found after fulfillment.");
    }

    private async Task<BillingPurchaseInfo> ApplyCorrectionCoreAsync(
        Guid tenantId,
        Guid purchaseId,
        ManualCommerceCorrectionRequest request,
        CancellationToken ct)
    {
        var reason = RequireReason(request);
        var idempotencyKey = BillingPriceReferenceInfo.NormalizeRequired(request.IdempotencyKey, nameof(request.IdempotencyKey));
        var purchase = await RequirePurchaseAsync(tenantId, purchaseId, ct).ConfigureAwait(false);
        var corrected = await _purchases.ApplyProviderUpdateAsync(
            purchaseId,
            new ApplyBillingPurchaseProviderUpdateRequest
            {
                ProviderName = BillingPriceReferenceInfo.NormalizeRequired(purchase.ProviderName ?? string.Empty, "providerName"),
                ProviderEventId = $"support-correction:{idempotencyKey}",
                EventType = "support.manual-correction",
                Status = BillingPurchaseStatuses.Normalize(request.Status),
                Metadata = new Dictionary<string, string> { ["correctionReason"] = reason }
            },
            ct).ConfigureAwait(false);
        if (string.Equals(corrected.Status, BillingPurchaseStatuses.Paid, StringComparison.OrdinalIgnoreCase))
        {
            await RunPaidHandlersAsync(corrected, ct).ConfigureAwait(false);
            corrected = await _purchases.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false)
                        ?? throw new BillingException("Purchase was not found after correction.");
        }
        return corrected;
    }

    private async Task<BillingPurchaseInfo> RequirePurchaseAsync(Guid tenantId, Guid purchaseId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || purchaseId == Guid.Empty)
            throw new BillingException("tenantId and purchaseId are required.");
        var purchase = await _purchases.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false)
            ?? throw new BillingException("Purchase was not found.");
        var claim = await _claimStore.GetByPurchaseAsync(purchaseId, ct).ConfigureAwait(false);
        var boundTenantId = purchase.TenantId ?? claim?.ClaimedTenantId;
        if (boundTenantId is not null && boundTenantId != tenantId)
            throw new BillingException("Purchase does not belong to the requested tenant.");
        return purchase;
    }

    private async Task RunPaidHandlersAsync(BillingPurchaseInfo purchase, CancellationToken ct)
    {
        foreach (var handler in _paidHandlers)
            await handler.HandlePaidPurchaseAsync(purchase, ct).ConfigureAwait(false);
    }

    private Task<T> ExecuteAsync<T>(Guid tenantId, Guid? entityId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => _operations.ExecuteAsync(this, action, new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = entityId }, ct);

    private static string RequireReason(CommerceRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = BillingPriceReferenceInfo.NormalizeRequired(request.Reason, nameof(request.Reason));
        if (reason.Length > 500)
            throw new BillingException("Reason cannot exceed 500 characters.");
        return reason;
    }

    private static CommercePurchaseSummary ToSummary(BillingPurchaseInfo purchase)
        => new(
            purchase.PurchaseId, purchase.CorrelationId, purchase.TenantId, purchase.UserId, purchase.LicenseKey,
            RedactEmail(purchase.BuyerEmail), purchase.OfferKey, purchase.PlanKey, purchase.TotalSeats, purchase.Currency,
            purchase.AmountTotal, purchase.Status, purchase.ProviderName, purchase.ProviderSubscriptionId,
            purchase.CreatedUtc, purchase.UpdatedUtc, purchase.ExpiresUtc);

    private static string? RedactEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        var parts = email.Split('@', 2);
        return parts.Length == 2 ? $"{parts[0][0]}***@{parts[1]}" : "***";
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(IReadOnlyDictionary<string, string> source, string key, string value)
    {
        var metadata = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase) { [key] = value };
        return metadata;
    }

    private static string? PurchaseStatusFor(string eventType)
        => BillingModes.NormalizeKnown(eventType, string.Empty) switch
        {
            BillingCommerceEventTypes.CheckoutCompleted => BillingPurchaseStatuses.Paid,
            BillingCommerceEventTypes.PaymentSucceeded => BillingPurchaseStatuses.Paid,
            BillingCommerceEventTypes.PaymentFailed => BillingPurchaseStatuses.Failed,
            BillingCommerceEventTypes.SubscriptionCanceled => BillingPurchaseStatuses.Canceled,
            BillingCommerceEventTypes.PaymentRefunded => BillingPurchaseStatuses.Refunded,
            BillingCommerceEventTypes.PaymentDisputed => BillingPurchaseStatuses.Refunded,
            _ => null
        };
}
