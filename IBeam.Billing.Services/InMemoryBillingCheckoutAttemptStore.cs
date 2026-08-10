using System.Collections.Concurrent;

namespace IBeam.Billing.Services;

public sealed class InMemoryBillingCheckoutAttemptStore : IBillingCheckoutAttemptStore
{
    private readonly ConcurrentDictionary<Guid, BillingCheckoutAttemptInfo> _attempts = [];

    public Task<IReadOnlyList<BillingCheckoutAttemptInfo>> ListAttemptsAsync(Guid purchaseId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BillingCheckoutAttemptInfo>>(
            _attempts.Values.Where(x => x.PurchaseId == purchaseId).OrderBy(x => x.CreatedUtc).ToList());

    public Task<BillingCheckoutAttemptInfo> SaveAttemptAsync(BillingCheckoutAttemptInfo attempt, CancellationToken ct = default)
        => Task.FromResult(_attempts.GetOrAdd(attempt.CheckoutAttemptId, attempt));
}
