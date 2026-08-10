using IBeam.Services.Abstractions;

namespace IBeam.Billing.Services;

[IBeamOperation("billing.purchases")]
public sealed class BillingPurchaseService : IBillingPurchaseService
{
    private readonly IBillingPurchaseStore _store;
    private readonly IServiceOperationExecutor _operations;
    private readonly TimeProvider _timeProvider;

    public BillingPurchaseService(
        IBillingPurchaseStore store,
        IServiceOperationExecutor? operations = null,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _operations = operations ?? new ServiceOperationExecutor();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [IBeamOperation("billing.purchases.get")]
    public async Task<BillingPurchaseInfo?> GetPurchaseAsync(Guid purchaseId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GetPurchaseCoreAsync(purchaseId, token),
            new ServiceOperationExecutionOptions { EntityId = purchaseId },
            ct).ConfigureAwait(false);

    private async Task<BillingPurchaseInfo?> GetPurchaseCoreAsync(Guid purchaseId, CancellationToken ct)
    {
        ValidatePurchaseId(purchaseId);
        var record = await _store.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false);
        return record?.ToInfo();
    }

    [IBeamOperation("billing.purchases.create")]
    public async Task<BillingPurchaseInfo> CreatePendingPurchaseAsync(
        CreatePendingBillingPurchaseRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => CreatePendingPurchaseCoreAsync(request, token),
            new ServiceOperationExecutionOptions { EntityId = request?.CorrelationId },
            ct).ConfigureAwait(false);

    private async Task<BillingPurchaseInfo> CreatePendingPurchaseCoreAsync(
        CreatePendingBillingPurchaseRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CorrelationId == Guid.Empty)
            throw new BillingException("correlationId is required.");

        var existing = await _store.GetPurchaseByCorrelationIdAsync(request.CorrelationId, ct).ConfigureAwait(false);
        if (existing is not null)
            return existing.ToInfo();

        var buyerEmail = BillingPurchaseInfo.NormalizeEmail(request.BuyerEmail)
            ?? throw new BillingException("buyerEmail is required.");
        if (request.TotalSeats <= 0)
            throw new BillingException("totalSeats must be positive.");
        if (request.AmountSubtotal < 0m || request.AmountTax < 0m || request.AmountTotal < 0m)
            throw new BillingException("Purchase amounts cannot be negative.");
        if (request.AmountSubtotal + request.AmountTax != request.AmountTotal)
            throw new BillingException("amountTotal must equal amountSubtotal plus amountTax.");

        var now = _timeProvider.GetUtcNow();
        if (request.ExpiresUtc is { } expiresUtc && expiresUtc <= now)
            throw new BillingException("expiresUtc must be in the future.");

        var record = new BillingPurchaseRecord(
            PurchaseId: Guid.NewGuid(),
            CorrelationId: request.CorrelationId,
            TenantId: null,
            UserId: null,
            LicenseKey: null,
            BuyerEmail: buyerEmail,
            OfferKey: BillingPriceReferenceInfo.NormalizeRequired(request.OfferKey, nameof(request.OfferKey)),
            ProductKey: BillingPriceReferenceInfo.NormalizeRequired(request.ProductKey, nameof(request.ProductKey)),
            PlanKey: BillingPriceReferenceInfo.NormalizeRequired(request.PlanKey, nameof(request.PlanKey)),
            TotalSeats: request.TotalSeats,
            Currency: BillingPriceReferenceInfo.NormalizeCurrency(request.Currency)
                      ?? throw new BillingException("currency is required."),
            AmountSubtotal: request.AmountSubtotal,
            AmountTax: request.AmountTax,
            AmountTotal: request.AmountTotal,
            Status: BillingPurchaseStatuses.Initiated,
            ProviderName: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderName)?.ToLowerInvariant(),
            ProviderCheckoutSessionId: null,
            ProviderCustomerId: null,
            ProviderSubscriptionId: null,
            LastProviderEventId: null,
            CreatedUtc: now,
            UpdatedUtc: now,
            PaidUtc: null,
            FulfilledUtc: null,
            ClaimedUtc: null,
            ExpiresUtc: request.ExpiresUtc,
            CanceledUtc: null,
            RefundedUtc: null,
            FailedUtc: null,
            BuyerEmailRedactedUtc: null,
            Metadata: BillingPriceReferenceInfo.NormalizeMetadata(request.Metadata));

        return (await _store.SavePurchaseAsync(record, ct: ct).ConfigureAwait(false)).ToInfo();
    }

    [IBeamOperation("billing.purchases.apply-provider-update")]
    public async Task<BillingPurchaseInfo> ApplyProviderUpdateAsync(
        Guid purchaseId,
        ApplyBillingPurchaseProviderUpdateRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ApplyProviderUpdateCoreAsync(purchaseId, request, token),
            new ServiceOperationExecutionOptions { EntityId = purchaseId },
            ct).ConfigureAwait(false);

    private async Task<BillingPurchaseInfo> ApplyProviderUpdateCoreAsync(
        Guid purchaseId,
        ApplyBillingPurchaseProviderUpdateRequest request,
        CancellationToken ct)
    {
        ValidatePurchaseId(purchaseId);
        ArgumentNullException.ThrowIfNull(request);

        var providerName = BillingPriceReferenceInfo.NormalizeRequired(request.ProviderName, nameof(request.ProviderName)).ToLowerInvariant();
        var providerEventId = BillingPriceReferenceInfo.NormalizeRequired(request.ProviderEventId, nameof(request.ProviderEventId));
        _ = BillingPriceReferenceInfo.NormalizeRequired(request.EventType, nameof(request.EventType));
        var duplicate = await _store.GetPurchaseByProviderEventAsync(providerName, providerEventId, ct).ConfigureAwait(false);
        if (duplicate is not null)
            return duplicate.ToInfo();

        var existing = await _store.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false)
                       ?? throw new BillingException("Purchase was not found.");
        if (!string.IsNullOrWhiteSpace(existing.ProviderName) &&
            !string.Equals(existing.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException("Provider update does not match the purchase provider.");
        }

        var nextStatus = BillingPurchaseStatuses.Normalize(request.Status);
        BillingPurchaseStatuses.RequireTransition(existing.Status, nextStatus);
        var occurredUtc = request.OccurredUtc ?? _timeProvider.GetUtcNow();
        var updated = existing with
        {
            Status = nextStatus,
            ProviderName = providerName,
            ProviderCheckoutSessionId = BillingPriceReferenceInfo.NormalizeOptional(request.ProviderCheckoutSessionId) ?? existing.ProviderCheckoutSessionId,
            ProviderCustomerId = BillingPriceReferenceInfo.NormalizeOptional(request.ProviderCustomerId) ?? existing.ProviderCustomerId,
            ProviderSubscriptionId = BillingPriceReferenceInfo.NormalizeOptional(request.ProviderSubscriptionId) ?? existing.ProviderSubscriptionId,
            LastProviderEventId = providerEventId,
            UpdatedUtc = occurredUtc,
            PaidUtc = nextStatus == BillingPurchaseStatuses.Paid ? occurredUtc : existing.PaidUtc,
            CanceledUtc = nextStatus == BillingPurchaseStatuses.Canceled ? occurredUtc : existing.CanceledUtc,
            RefundedUtc = nextStatus == BillingPurchaseStatuses.Refunded ? occurredUtc : existing.RefundedUtc,
            FailedUtc = nextStatus == BillingPurchaseStatuses.Failed ? occurredUtc : existing.FailedUtc,
            Metadata = MergeMetadata(existing.Metadata, request.Metadata)
        };

        return (await _store.SavePurchaseAsync(updated, providerEventId, existing.UpdatedUtc, ct).ConfigureAwait(false)).ToInfo();
    }

    [IBeamOperation("billing.purchases.fulfill")]
    public async Task<BillingPurchaseInfo> FulfillPaidPurchaseAsync(
        Guid purchaseId,
        Guid licenseKey,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => FulfillPaidPurchaseCoreAsync(purchaseId, licenseKey, token),
            new ServiceOperationExecutionOptions { EntityId = purchaseId },
            ct).ConfigureAwait(false);

    private async Task<BillingPurchaseInfo> FulfillPaidPurchaseCoreAsync(
        Guid purchaseId,
        Guid licenseKey,
        CancellationToken ct)
    {
        ValidatePurchaseId(purchaseId);
        if (licenseKey == Guid.Empty)
            throw new BillingException("licenseKey is required.");

        var existing = await _store.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false)
                       ?? throw new BillingException("Purchase was not found.");
        if (string.Equals(existing.Status, BillingPurchaseStatuses.Fulfilled, StringComparison.OrdinalIgnoreCase))
        {
            if (existing.LicenseKey != licenseKey)
                throw new BillingException("Purchase is already fulfilled by a different license.");
            return existing.ToInfo();
        }
        if (!string.Equals(existing.Status, BillingPurchaseStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            throw new BillingException("Only a paid purchase can be fulfilled.");

        var now = _timeProvider.GetUtcNow();
        var fulfilled = existing with
        {
            LicenseKey = licenseKey,
            Status = BillingPurchaseStatuses.Fulfilled,
            FulfilledUtc = now,
            UpdatedUtc = now
        };
        return (await _store.SavePurchaseAsync(fulfilled, expectedUpdatedUtc: existing.UpdatedUtc, ct: ct).ConfigureAwait(false)).ToInfo();
    }

    [IBeamOperation("billing.purchases.redact-buyer")]
    public async Task RedactBuyerEmailAsync(Guid purchaseId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => RedactBuyerEmailCoreAsync(purchaseId, token),
            new ServiceOperationExecutionOptions { EntityId = purchaseId },
            ct).ConfigureAwait(false);

    private async Task RedactBuyerEmailCoreAsync(Guid purchaseId, CancellationToken ct)
    {
        ValidatePurchaseId(purchaseId);
        var existing = await _store.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false)
                       ?? throw new BillingException("Purchase was not found.");
        if (existing.BuyerEmail is null)
            return;

        var now = _timeProvider.GetUtcNow();
        await _store.SavePurchaseAsync(existing with
        {
            BuyerEmail = null,
            BuyerEmailRedactedUtc = now,
            UpdatedUtc = now
        }, expectedUpdatedUtc: existing.UpdatedUtc, ct: ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> existing,
        IReadOnlyDictionary<string, string>? updates)
    {
        var result = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var item in BillingPriceReferenceInfo.NormalizeMetadata(updates))
            result[item.Key] = item.Value;
        return result;
    }

    private static void ValidatePurchaseId(Guid purchaseId)
    {
        if (purchaseId == Guid.Empty)
            throw new BillingException("purchaseId is required.");
    }
}
