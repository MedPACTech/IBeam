namespace IBeam.Billing;

public interface IBillingCustomerService
{
    Task<IReadOnlyList<BillingCustomerInfo>> ListCustomersAsync(Guid tenantId, CancellationToken ct = default);
    Task<BillingCustomerInfo?> GetCustomerAsync(Guid tenantId, Guid billingCustomerId, CancellationToken ct = default);
    Task<BillingCustomerInfo> CreateCustomerAsync(Guid tenantId, CreateBillingCustomerRequest request, CancellationToken ct = default);
    Task<BillingCustomerInfo> UpdateCustomerAsync(Guid tenantId, Guid billingCustomerId, UpdateBillingCustomerRequest request, CancellationToken ct = default);
}

public interface IBillingSubscriptionService
{
    Task<IReadOnlyList<BillingSubscriptionInfo>> ListSubscriptionsAsync(Guid tenantId, CancellationToken ct = default);
    Task<BillingSubscriptionInfo?> GetSubscriptionAsync(Guid tenantId, Guid billingSubscriptionId, CancellationToken ct = default);
    Task<BillingSubscriptionInfo> CreateSubscriptionAsync(Guid tenantId, CreateBillingSubscriptionRequest request, CancellationToken ct = default);
    Task<BillingSubscriptionInfo> UpdateSubscriptionAsync(Guid tenantId, Guid billingSubscriptionId, UpdateBillingSubscriptionRequest request, CancellationToken ct = default);
}

public interface IBillingInvoiceService
{
    Task<IReadOnlyList<BillingInvoiceInfo>> ListInvoicesAsync(Guid tenantId, CancellationToken ct = default);
    Task<BillingInvoiceInfo?> GetInvoiceAsync(Guid tenantId, Guid billingInvoiceId, CancellationToken ct = default);
    Task<BillingInvoiceInfo> CreateInvoiceAsync(Guid tenantId, CreateBillingInvoiceRequest request, CancellationToken ct = default);
    Task<BillingInvoiceInfo> UpdateInvoiceAsync(Guid tenantId, Guid billingInvoiceId, UpdateBillingInvoiceRequest request, CancellationToken ct = default);
}

public interface IBillingProviderEventService
{
    Task<BillingProviderEventInfo> RecordEventAsync(RecordBillingProviderEventRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BillingProviderEventInfo>> ListEventsAsync(Guid? tenantId = null, CancellationToken ct = default);
}

public interface IBillingStore
{
    Task<IReadOnlyList<BillingCustomerRecord>> ListCustomersAsync(Guid tenantId, CancellationToken ct = default);
    Task<BillingCustomerRecord?> GetCustomerAsync(Guid tenantId, Guid billingCustomerId, CancellationToken ct = default);
    Task<BillingCustomerRecord> SaveCustomerAsync(BillingCustomerRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<BillingSubscriptionRecord>> ListSubscriptionsAsync(Guid tenantId, CancellationToken ct = default);
    Task<BillingSubscriptionRecord?> GetSubscriptionAsync(Guid tenantId, Guid billingSubscriptionId, CancellationToken ct = default);
    Task<BillingSubscriptionRecord> SaveSubscriptionAsync(BillingSubscriptionRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<BillingInvoiceRecord>> ListInvoicesAsync(Guid tenantId, CancellationToken ct = default);
    Task<BillingInvoiceRecord?> GetInvoiceAsync(Guid tenantId, Guid billingInvoiceId, CancellationToken ct = default);
    Task<BillingInvoiceRecord> SaveInvoiceAsync(BillingInvoiceRecord record, CancellationToken ct = default);

    Task<BillingProviderEventRecord?> GetProviderEventByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<BillingProviderEventRecord> SaveProviderEventAsync(BillingProviderEventRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<BillingProviderEventRecord>> ListProviderEventsAsync(Guid? tenantId = null, CancellationToken ct = default);
}
