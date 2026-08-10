using System.Collections.Concurrent;

namespace IBeam.Billing.Services;

public sealed class InMemoryBillingPurchaseStore : IBillingPurchaseStore
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<Guid, BillingPurchaseRecord> _purchases = [];
    private readonly ConcurrentDictionary<Guid, Guid> _purchaseIdsByCorrelationId = [];
    private readonly ConcurrentDictionary<string, Guid> _purchaseIdsByProviderEvent = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, Guid> _purchaseIdsByLicenseKey = [];

    public Task<BillingPurchaseRecord?> GetPurchaseAsync(Guid purchaseId, CancellationToken ct = default)
    {
        _purchases.TryGetValue(purchaseId, out var purchase);
        return Task.FromResult(purchase);
    }

    public Task<BillingPurchaseRecord?> GetPurchaseByCorrelationIdAsync(Guid correlationId, CancellationToken ct = default)
    {
        if (_purchaseIdsByCorrelationId.TryGetValue(correlationId, out var purchaseId) &&
            _purchases.TryGetValue(purchaseId, out var purchase))
        {
            return Task.FromResult<BillingPurchaseRecord?>(purchase);
        }

        return Task.FromResult<BillingPurchaseRecord?>(null);
    }

    public Task<BillingPurchaseRecord?> GetPurchaseByProviderEventAsync(
        string providerName,
        string providerEventId,
        CancellationToken ct = default)
    {
        var key = ProviderEventKey(providerName, providerEventId);
        if (_purchaseIdsByProviderEvent.TryGetValue(key, out var purchaseId) &&
            _purchases.TryGetValue(purchaseId, out var purchase))
        {
            return Task.FromResult<BillingPurchaseRecord?>(purchase);
        }

        return Task.FromResult<BillingPurchaseRecord?>(null);
    }

    public Task<BillingPurchaseRecord?> GetPurchaseByLicenseKeyAsync(Guid licenseKey, CancellationToken ct = default)
    {
        if (_purchaseIdsByLicenseKey.TryGetValue(licenseKey, out var purchaseId) &&
            _purchases.TryGetValue(purchaseId, out var purchase))
        {
            return Task.FromResult<BillingPurchaseRecord?>(purchase);
        }

        return Task.FromResult<BillingPurchaseRecord?>(null);
    }

    public Task<BillingPurchaseRecord> SavePurchaseAsync(
        BillingPurchaseRecord record,
        string? providerEventId = null,
        DateTimeOffset? expectedUpdatedUtc = null,
        CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (expectedUpdatedUtc is { } expected &&
                _purchases.TryGetValue(record.PurchaseId, out var current) &&
                current.UpdatedUtc != expected)
            {
                throw new BillingException("Purchase changed concurrently; reload it before retrying.");
            }

            var canonicalPurchaseId = _purchaseIdsByCorrelationId.GetOrAdd(record.CorrelationId, record.PurchaseId);
            if (canonicalPurchaseId != record.PurchaseId && _purchases.TryGetValue(canonicalPurchaseId, out var existing))
                return Task.FromResult(existing);

            if (!string.IsNullOrWhiteSpace(providerEventId) && !string.IsNullOrWhiteSpace(record.ProviderName))
            {
                var eventKey = ProviderEventKey(record.ProviderName, providerEventId);
                var eventPurchaseId = _purchaseIdsByProviderEvent.GetOrAdd(eventKey, record.PurchaseId);
                if (eventPurchaseId != record.PurchaseId && _purchases.TryGetValue(eventPurchaseId, out var eventPurchase))
                    return Task.FromResult(eventPurchase);
            }

            _purchases[record.PurchaseId] = record;
            if (record.LicenseKey is { } licenseKey)
                _purchaseIdsByLicenseKey[licenseKey] = record.PurchaseId;
            return Task.FromResult(record);
        }
    }

    public Task<int> DeleteExpiredPurchasesAsync(DateTimeOffset cutoffUtc, int maxCount = 100, CancellationToken ct = default)
    {
        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount));

        var expired = _purchases.Values
            .Where(x => x.ExpiresUtc <= cutoffUtc && x.Status is BillingPurchaseStatuses.Initiated or BillingPurchaseStatuses.AwaitingPayment or BillingPurchaseStatuses.Expired)
            .OrderBy(x => x.ExpiresUtc)
            .Take(maxCount)
            .ToList();
        foreach (var purchase in expired)
        {
            _purchases.TryRemove(purchase.PurchaseId, out _);
            _purchaseIdsByCorrelationId.TryRemove(purchase.CorrelationId, out _);
            if (purchase.LicenseKey is { } licenseKey)
                _purchaseIdsByLicenseKey.TryRemove(licenseKey, out _);
            if (!string.IsNullOrWhiteSpace(purchase.ProviderName) && !string.IsNullOrWhiteSpace(purchase.LastProviderEventId))
                _purchaseIdsByProviderEvent.TryRemove(ProviderEventKey(purchase.ProviderName, purchase.LastProviderEventId), out _);
        }

        return Task.FromResult(expired.Count);
    }

    private static string ProviderEventKey(string providerName, string providerEventId)
        => $"{BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName)).ToLowerInvariant()}:{BillingPriceReferenceInfo.NormalizeRequired(providerEventId, nameof(providerEventId))}";
}
