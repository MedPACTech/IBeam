using System.Collections.Concurrent;

namespace IBeam.Billing.Services;

public sealed class InMemoryBillingPurchaseStore : IBillingPurchaseStore
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<Guid, BillingPurchaseRecord> _purchases = [];
    private readonly ConcurrentDictionary<Guid, Guid> _purchaseIdsByCorrelationId = [];
    private readonly ConcurrentDictionary<string, Guid> _purchaseIdsByProviderEvent = new(StringComparer.OrdinalIgnoreCase);

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

    public Task<BillingPurchaseRecord> SavePurchaseAsync(
        BillingPurchaseRecord record,
        string? providerEventId = null,
        CancellationToken ct = default)
    {
        lock (_sync)
        {
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
            return Task.FromResult(record);
        }
    }

    private static string ProviderEventKey(string providerName, string providerEventId)
        => $"{BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName)).ToLowerInvariant()}:{BillingPriceReferenceInfo.NormalizeRequired(providerEventId, nameof(providerEventId))}";
}
