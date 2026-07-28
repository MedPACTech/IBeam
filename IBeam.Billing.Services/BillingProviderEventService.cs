using IBeam.Services.Abstractions;

namespace IBeam.Billing.Services;

[IBeamOperation("billing.provider-events")]
public sealed class BillingProviderEventService : IBillingProviderEventService
{
    private readonly IBillingStore _store;
    private readonly IServiceOperationExecutor _operations;

    public BillingProviderEventService(IBillingStore store, IServiceOperationExecutor? operations = null)
    {
        _store = store;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("billing.provider-events.record")]
    public async Task<BillingProviderEventInfo> RecordEventAsync(RecordBillingProviderEventRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => RecordEventCoreAsync(request, token),
            new ServiceOperationExecutionOptions { TenantId = request?.TenantId, EntityId = request?.TenantId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.provider-events.list")]
    public async Task<IReadOnlyList<BillingProviderEventInfo>> ListEventsAsync(Guid? tenantId = null, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListEventsCoreAsync(tenantId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    private async Task<BillingProviderEventInfo> RecordEventCoreAsync(RecordBillingProviderEventRequest request, CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var providerName = BillingServiceValidation.Required(request.ProviderName, nameof(request.ProviderName));
        var providerEventId = BillingServiceValidation.Required(request.ProviderEventId, nameof(request.ProviderEventId));
        var idempotencyKey = BillingProviderEventInfo.CreateIdempotencyKey(providerName, providerEventId);
        var existing = await _store.GetProviderEventByIdempotencyKeyAsync(idempotencyKey, ct).ConfigureAwait(false);
        if (existing is not null)
            return BillingProviderEventInfo.FromRecord(existing);

        var record = new BillingProviderEventRecord(
            BillingProviderEventId: Guid.NewGuid(),
            ProviderName: providerName,
            ProviderEventId: providerEventId,
            EventType: BillingServiceValidation.Required(request.EventType, nameof(request.EventType)),
            Status: BillingProviderEventStatuses.Normalize(request.Status ?? BillingProviderEventStatuses.Received),
            ReceivedUtc: DateTimeOffset.UtcNow,
            ProcessedUtc: null,
            TenantId: request.TenantId == Guid.Empty ? null : request.TenantId,
            UserId: request.UserId == Guid.Empty ? null : request.UserId,
            ProviderCustomerId: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderCustomerId),
            ProviderSubscriptionId: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderSubscriptionId),
            ProviderInvoiceId: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderInvoiceId),
            PayloadContentType: BillingPriceReferenceInfo.NormalizeOptional(request.PayloadContentType),
            PayloadReference: BillingPriceReferenceInfo.NormalizeOptional(request.PayloadReference),
            Metadata: BillingPriceReferenceInfo.NormalizeMetadata(request.Metadata));

        var saved = await _store.SaveProviderEventAsync(record, ct).ConfigureAwait(false);
        return BillingProviderEventInfo.FromRecord(saved);
    }

    private async Task<IReadOnlyList<BillingProviderEventInfo>> ListEventsCoreAsync(Guid? tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            tenantId = null;

        var records = await _store.ListProviderEventsAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(BillingProviderEventInfo.FromRecord).ToList();
    }
}
