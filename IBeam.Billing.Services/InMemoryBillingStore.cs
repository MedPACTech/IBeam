using System.Collections.Concurrent;

namespace IBeam.Billing.Services;

public sealed class InMemoryBillingStore : IBillingStore
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid CustomerId), BillingCustomerRecord> _customers = [];
    private readonly ConcurrentDictionary<(Guid TenantId, Guid SubscriptionId), BillingSubscriptionRecord> _subscriptions = [];
    private readonly ConcurrentDictionary<(Guid TenantId, Guid InvoiceId), BillingInvoiceRecord> _invoices = [];
    private readonly ConcurrentDictionary<Guid, BillingProviderEventRecord> _events = [];
    private readonly ConcurrentDictionary<string, Guid> _eventIdsByIdempotencyKey = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<BillingCustomerRecord>> ListCustomersAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BillingCustomerRecord>>(
            _customers.Values
                .Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());

    public Task<BillingCustomerRecord?> GetCustomerAsync(Guid tenantId, Guid billingCustomerId, CancellationToken ct = default)
    {
        _customers.TryGetValue((tenantId, billingCustomerId), out var customer);
        return Task.FromResult(customer);
    }

    public Task<BillingCustomerRecord> SaveCustomerAsync(BillingCustomerRecord record, CancellationToken ct = default)
    {
        _customers[(record.TenantId, record.BillingCustomerId)] = record;
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<BillingSubscriptionRecord>> ListSubscriptionsAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BillingSubscriptionRecord>>(
            _subscriptions.Values
                .Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.CreatedUtc)
                .ToList());

    public Task<BillingSubscriptionRecord?> GetSubscriptionAsync(Guid tenantId, Guid billingSubscriptionId, CancellationToken ct = default)
    {
        _subscriptions.TryGetValue((tenantId, billingSubscriptionId), out var subscription);
        return Task.FromResult(subscription);
    }

    public Task<BillingSubscriptionRecord> SaveSubscriptionAsync(BillingSubscriptionRecord record, CancellationToken ct = default)
    {
        _subscriptions[(record.TenantId, record.BillingSubscriptionId)] = record;
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<BillingInvoiceRecord>> ListInvoicesAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BillingInvoiceRecord>>(
            _invoices.Values
                .Where(x => x.TenantId == tenantId)
                .OrderByDescending(x => x.CreatedUtc)
                .ToList());

    public Task<BillingInvoiceRecord?> GetInvoiceAsync(Guid tenantId, Guid billingInvoiceId, CancellationToken ct = default)
    {
        _invoices.TryGetValue((tenantId, billingInvoiceId), out var invoice);
        return Task.FromResult(invoice);
    }

    public Task<BillingInvoiceRecord> SaveInvoiceAsync(BillingInvoiceRecord record, CancellationToken ct = default)
    {
        _invoices[(record.TenantId, record.BillingInvoiceId)] = record;
        return Task.FromResult(record);
    }

    public Task<BillingProviderEventRecord?> GetProviderEventByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        if (_eventIdsByIdempotencyKey.TryGetValue(idempotencyKey, out var eventId) &&
            _events.TryGetValue(eventId, out var record))
        {
            return Task.FromResult<BillingProviderEventRecord?>(record);
        }

        return Task.FromResult<BillingProviderEventRecord?>(null);
    }

    public Task<BillingProviderEventRecord> SaveProviderEventAsync(BillingProviderEventRecord record, CancellationToken ct = default)
    {
        var eventId = _eventIdsByIdempotencyKey.GetOrAdd(record.IdempotencyKey, record.BillingProviderEventId);
        if (eventId != record.BillingProviderEventId &&
            _events.TryGetValue(eventId, out var existing))
        {
            return Task.FromResult(existing);
        }

        _events[record.BillingProviderEventId] = record;
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<BillingProviderEventRecord>> ListProviderEventsAsync(Guid? tenantId = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BillingProviderEventRecord>>(
            _events.Values
                .Where(x => tenantId is null || x.TenantId == tenantId)
                .OrderByDescending(x => x.ReceivedUtc)
                .ToList());
}
