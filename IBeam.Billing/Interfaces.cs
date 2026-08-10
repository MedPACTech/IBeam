namespace IBeam.Billing;

public interface IBillingOfferCatalogProvider
{
    Task<IReadOnlyList<BillingOfferInfo>> ListOffersAsync(CancellationToken ct = default);
    Task<BillingOfferInfo?> GetOfferAsync(string offerKey, CancellationToken ct = default);
}

public interface IBillingPurchaseService
{
    Task<BillingPurchaseInfo?> GetPurchaseAsync(Guid purchaseId, CancellationToken ct = default);
    Task<BillingPurchaseInfo> CreatePendingPurchaseAsync(CreatePendingBillingPurchaseRequest request, CancellationToken ct = default);
    Task<BillingPurchaseInfo> ApplyProviderUpdateAsync(Guid purchaseId, ApplyBillingPurchaseProviderUpdateRequest request, CancellationToken ct = default);
    Task<BillingPurchaseInfo> FulfillPaidPurchaseAsync(Guid purchaseId, Guid licenseKey, CancellationToken ct = default);
    Task<BillingPurchaseInfo> MarkClaimedAsync(Guid purchaseId, Guid tenantId, Guid userId, CancellationToken ct = default);
    Task RedactBuyerEmailAsync(Guid purchaseId, CancellationToken ct = default);
}

public interface IBillingPaidPurchaseHandler
{
    Task HandlePaidPurchaseAsync(BillingPurchaseInfo purchase, CancellationToken ct = default);
}

public interface IBillingPurchaseStore
{
    Task<BillingPurchaseRecord?> GetPurchaseAsync(Guid purchaseId, CancellationToken ct = default);
    Task<BillingPurchaseRecord?> GetPurchaseByCorrelationIdAsync(Guid correlationId, CancellationToken ct = default);
    Task<BillingPurchaseRecord?> GetPurchaseByProviderEventAsync(string providerName, string providerEventId, CancellationToken ct = default);
    Task<BillingPurchaseRecord?> GetPurchaseByLicenseKeyAsync(Guid licenseKey, CancellationToken ct = default);
    Task<BillingPurchaseRecord> SavePurchaseAsync(
        BillingPurchaseRecord record,
        string? providerEventId = null,
        DateTimeOffset? expectedUpdatedUtc = null,
        CancellationToken ct = default);
    Task<int> DeleteExpiredPurchasesAsync(DateTimeOffset cutoffUtc, int maxCount = 100, CancellationToken ct = default);
}

public interface IBillingCheckoutAttemptStore
{
    Task<IReadOnlyList<BillingCheckoutAttemptInfo>> ListAttemptsAsync(Guid purchaseId, CancellationToken ct = default);
    Task<BillingCheckoutAttemptInfo> SaveAttemptAsync(BillingCheckoutAttemptInfo attempt, CancellationToken ct = default);
}

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
    Task<BillingProviderEventInfo?> GetEventAsync(string providerName, string providerEventId, CancellationToken ct = default);
    Task<BillingProviderEventInfo> RecordEventAsync(RecordBillingProviderEventRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BillingProviderEventInfo>> ListEventsAsync(Guid? tenantId = null, CancellationToken ct = default);
}

public interface IBillingSubscriptionProviderBindingStore
{
    Task<BillingProviderMigrationRecord?> GetMigrationAsync(
        Guid tenantId,
        Guid billingSubscriptionId,
        string targetProviderName,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<IReadOnlyList<BillingSubscriptionProviderBindingInfo>> ListBindingsAsync(
        Guid tenantId,
        Guid billingSubscriptionId,
        CancellationToken ct = default);

    Task<BillingProviderMigrationRecord> CommitMigrationAsync(
        BillingProviderMigrationRecord migration,
        BillingSubscriptionProviderBindingInfo sourceBinding,
        BillingSubscriptionProviderBindingInfo targetBinding,
        CancellationToken ct = default);
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
