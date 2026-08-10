using System.Collections.Concurrent;

namespace IBeam.Billing.Licensing;

public sealed class InMemoryBillingPurchaseClaimStore : IBillingPurchaseClaimStore
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, BillingPurchaseClaimRecord> _claimsByHash = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> _hashesByPurchase = [];

    public Task<BillingPurchaseClaimRecord?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        _claimsByHash.TryGetValue(tokenHash, out var claim);
        return Task.FromResult(claim);
    }

    public Task<BillingPurchaseClaimRecord?> GetByPurchaseAsync(Guid purchaseId, CancellationToken ct = default)
    {
        if (_hashesByPurchase.TryGetValue(purchaseId, out var hash) && _claimsByHash.TryGetValue(hash, out var claim))
            return Task.FromResult<BillingPurchaseClaimRecord?>(claim);
        return Task.FromResult<BillingPurchaseClaimRecord?>(null);
    }

    public Task<BillingPurchaseClaimRecord> SaveIssuedAsync(BillingPurchaseClaimRecord record, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (_hashesByPurchase.TryGetValue(record.PurchaseId, out var oldHash) &&
                _claimsByHash.TryGetValue(oldHash, out var existing) &&
                existing.ClaimedUtc is not null)
            {
                throw new BillingException("Purchase has already been claimed.");
            }

            if (oldHash is not null)
                _claimsByHash.TryRemove(oldHash, out _);
            _claimsByHash[record.TokenHash] = record;
            _hashesByPurchase[record.PurchaseId] = record.TokenHash;
            return Task.FromResult(record);
        }
    }

    public Task<BillingPurchaseClaimRecord?> TryClaimAsync(
        string tokenHash,
        Guid tenantId,
        Guid userId,
        DateTimeOffset claimedUtc,
        CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (!_claimsByHash.TryGetValue(tokenHash, out var claim))
                return Task.FromResult<BillingPurchaseClaimRecord?>(null);
            if (claim.ClaimedUtc is not null)
                return Task.FromResult<BillingPurchaseClaimRecord?>(claim);

            var claimed = claim with
            {
                ClaimedTenantId = tenantId,
                ClaimedUserId = userId,
                ClaimedUtc = claimedUtc
            };
            _claimsByHash[tokenHash] = claimed;
            return Task.FromResult<BillingPurchaseClaimRecord?>(claimed);
        }
    }
}
