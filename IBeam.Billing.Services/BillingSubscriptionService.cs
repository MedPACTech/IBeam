using IBeam.Services.Abstractions;

namespace IBeam.Billing.Services;

[IBeamOperation("billing.subscriptions")]
public sealed class BillingSubscriptionService : IBillingSubscriptionService
{
    private readonly IBillingStore _store;
    private readonly IServiceOperationExecutor _operations;

    public BillingSubscriptionService(IBillingStore store, IServiceOperationExecutor? operations = null)
    {
        _store = store;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("billing.subscriptions.list")]
    public async Task<IReadOnlyList<BillingSubscriptionInfo>> ListSubscriptionsAsync(Guid tenantId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListSubscriptionsCoreAsync(tenantId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.subscriptions.get")]
    public async Task<BillingSubscriptionInfo?> GetSubscriptionAsync(Guid tenantId, Guid billingSubscriptionId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GetSubscriptionCoreAsync(tenantId, billingSubscriptionId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = billingSubscriptionId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.subscriptions.create")]
    public async Task<BillingSubscriptionInfo> CreateSubscriptionAsync(
        Guid tenantId,
        CreateBillingSubscriptionRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => CreateSubscriptionCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.BillingCustomerId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.subscriptions.update")]
    public async Task<BillingSubscriptionInfo> UpdateSubscriptionAsync(
        Guid tenantId,
        Guid billingSubscriptionId,
        UpdateBillingSubscriptionRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => UpdateSubscriptionCoreAsync(tenantId, billingSubscriptionId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = billingSubscriptionId },
            ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<BillingSubscriptionInfo>> ListSubscriptionsCoreAsync(Guid tenantId, CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        var records = await _store.ListSubscriptionsAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(BillingSubscriptionInfo.FromRecord).ToList();
    }

    private async Task<BillingSubscriptionInfo?> GetSubscriptionCoreAsync(Guid tenantId, Guid billingSubscriptionId, CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        BillingServiceValidation.ValidateId(billingSubscriptionId, nameof(billingSubscriptionId));
        var record = await _store.GetSubscriptionAsync(tenantId, billingSubscriptionId, ct).ConfigureAwait(false);
        return record is null ? null : BillingSubscriptionInfo.FromRecord(record);
    }

    private async Task<BillingSubscriptionInfo> CreateSubscriptionCoreAsync(
        Guid tenantId,
        CreateBillingSubscriptionRequest request,
        CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        BillingServiceValidation.ValidateId(request.BillingCustomerId, nameof(request.BillingCustomerId));
        var now = DateTimeOffset.UtcNow;
        var record = new BillingSubscriptionRecord(
            BillingSubscriptionId: Guid.NewGuid(),
            TenantId: tenantId,
            UserId: request.UserId == Guid.Empty ? null : request.UserId,
            BillingCustomerId: request.BillingCustomerId,
            ProductKey: BillingPriceReferenceInfo.NormalizeOptional(request.ProductKey),
            PlanKey: BillingPriceReferenceInfo.NormalizeOptional(request.PlanKey),
            BillingMode: BillingModes.Normalize(request.BillingMode),
            Status: BillingSubscriptionStatuses.Normalize(request.Status ?? BillingSubscriptionStatuses.Active),
            SeatQuantity: request.SeatQuantity,
            Price: request.Price,
            ProviderName: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderName),
            ProviderSubscriptionId: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderSubscriptionId),
            ProviderStatus: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderStatus),
            CurrentPeriodStartsUtc: request.CurrentPeriodStartsUtc,
            CurrentPeriodEndsUtc: request.CurrentPeriodEndsUtc,
            CancelAtPeriodEnd: request.CancelAtPeriodEnd,
            CreatedUtc: now,
            UpdatedUtc: now,
            Metadata: BillingPriceReferenceInfo.NormalizeMetadata(request.Metadata));

        var saved = await _store.SaveSubscriptionAsync(record, ct).ConfigureAwait(false);
        return BillingSubscriptionInfo.FromRecord(saved);
    }

    private async Task<BillingSubscriptionInfo> UpdateSubscriptionCoreAsync(
        Guid tenantId,
        Guid billingSubscriptionId,
        UpdateBillingSubscriptionRequest request,
        CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        BillingServiceValidation.ValidateId(billingSubscriptionId, nameof(billingSubscriptionId));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var existing = await GetRequiredAsync(tenantId, billingSubscriptionId, ct).ConfigureAwait(false);
        var updated = existing with
        {
            ProductKey = BillingServiceValidation.OptionalOrExisting(request.ProductKey, existing.ProductKey),
            PlanKey = BillingServiceValidation.OptionalOrExisting(request.PlanKey, existing.PlanKey),
            BillingMode = string.IsNullOrWhiteSpace(request.BillingMode) ? existing.BillingMode : BillingModes.Normalize(request.BillingMode),
            Status = string.IsNullOrWhiteSpace(request.Status) ? existing.Status : BillingSubscriptionStatuses.Normalize(request.Status),
            SeatQuantity = request.SeatQuantity ?? existing.SeatQuantity,
            Price = request.Price ?? existing.Price,
            ProviderName = BillingServiceValidation.OptionalOrExisting(request.ProviderName, existing.ProviderName),
            ProviderSubscriptionId = BillingServiceValidation.OptionalOrExisting(request.ProviderSubscriptionId, existing.ProviderSubscriptionId),
            ProviderStatus = BillingServiceValidation.OptionalOrExisting(request.ProviderStatus, existing.ProviderStatus),
            CurrentPeriodStartsUtc = request.CurrentPeriodStartsUtc ?? existing.CurrentPeriodStartsUtc,
            CurrentPeriodEndsUtc = request.CurrentPeriodEndsUtc ?? existing.CurrentPeriodEndsUtc,
            CancelAtPeriodEnd = request.CancelAtPeriodEnd ?? existing.CancelAtPeriodEnd,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Metadata = BillingServiceValidation.MetadataOrExisting(request.Metadata, existing.Metadata)
        };

        var saved = await _store.SaveSubscriptionAsync(updated, ct).ConfigureAwait(false);
        return BillingSubscriptionInfo.FromRecord(saved);
    }

    private async Task<BillingSubscriptionRecord> GetRequiredAsync(Guid tenantId, Guid billingSubscriptionId, CancellationToken ct)
    {
        var existing = await _store.GetSubscriptionAsync(tenantId, billingSubscriptionId, ct).ConfigureAwait(false);
        return existing ?? throw new BillingException($"Billing subscription '{billingSubscriptionId}' was not found for tenant '{tenantId}'.");
    }
}
