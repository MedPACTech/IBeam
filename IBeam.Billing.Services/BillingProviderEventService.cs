using IBeam.Services.Abstractions;

namespace IBeam.Billing.Services;

[IBeamOperation("billing.provider-events")]
public sealed class BillingProviderEventService : IBillingProviderEventService
{
    private readonly IBillingStore _store;
    private readonly IServiceOperationExecutor _operations;
    private readonly TimeProvider _timeProvider;

    public BillingProviderEventService(
        IBillingStore store,
        IServiceOperationExecutor? operations = null,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _operations = operations ?? new ServiceOperationExecutor();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BillingProviderEventInfo?> GetEventAsync(
        string providerName,
        string providerEventId,
        CancellationToken ct = default)
    {
        var idempotencyKey = BillingProviderEventInfo.CreateIdempotencyKey(providerName, providerEventId);
        var record = await _store.GetProviderEventByIdempotencyKeyAsync(idempotencyKey, ct).ConfigureAwait(false);
        return record?.ToInfo();
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
        var status = BillingProviderEventStatuses.Normalize(request.Status ?? BillingProviderEventStatuses.Received);
        if (existing is not null)
        {
            if (!CanUpdateOutcome(existing.Status, status))
                return existing.ToInfo();

            var retried = existing with
            {
                Status = status,
                ProcessedUtc = IsFinal(status) ? _timeProvider.GetUtcNow() : null,
                Metadata = MergeMetadata(existing.Metadata, request.Metadata)
            };
            return (await _store.SaveProviderEventAsync(retried, ct).ConfigureAwait(false)).ToInfo();
        }

        var record = new BillingProviderEventRecord(
            BillingProviderEventId: Guid.NewGuid(),
            ProviderName: providerName,
            ProviderEventId: providerEventId,
            EventType: BillingServiceValidation.Required(request.EventType, nameof(request.EventType)),
            Status: status,
            ReceivedUtc: _timeProvider.GetUtcNow(),
            ProcessedUtc: IsFinal(status) ? _timeProvider.GetUtcNow() : null,
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

    private static bool CanUpdateOutcome(string currentStatus, string nextStatus)
        => (string.Equals(currentStatus, BillingProviderEventStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentStatus, BillingProviderEventStatuses.Received, StringComparison.OrdinalIgnoreCase)) &&
           !string.Equals(currentStatus, nextStatus, StringComparison.OrdinalIgnoreCase);

    private static bool IsFinal(string status)
        => !string.Equals(status, BillingProviderEventStatuses.Received, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> existing,
        IReadOnlyDictionary<string, string>? changes)
    {
        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var item in BillingPriceReferenceInfo.NormalizeMetadata(changes))
            merged[item.Key] = item.Value;
        return merged;
    }

    private async Task<IReadOnlyList<BillingProviderEventInfo>> ListEventsCoreAsync(Guid? tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            tenantId = null;

        var records = await _store.ListProviderEventsAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(BillingProviderEventInfo.FromRecord).ToList();
    }
}
