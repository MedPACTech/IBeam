using IBeam.Services.Abstractions;

namespace IBeam.Billing.Services;

[IBeamOperation("billing.customers")]
public sealed class BillingCustomerService : IBillingCustomerService
{
    private readonly IBillingStore _store;
    private readonly IServiceOperationExecutor _operations;

    public BillingCustomerService(IBillingStore store, IServiceOperationExecutor? operations = null)
    {
        _store = store;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("billing.customers.list")]
    public async Task<IReadOnlyList<BillingCustomerInfo>> ListCustomersAsync(Guid tenantId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListCustomersCoreAsync(tenantId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.customers.get")]
    public async Task<BillingCustomerInfo?> GetCustomerAsync(Guid tenantId, Guid billingCustomerId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GetCustomerCoreAsync(tenantId, billingCustomerId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = billingCustomerId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.customers.create")]
    public async Task<BillingCustomerInfo> CreateCustomerAsync(Guid tenantId, CreateBillingCustomerRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => CreateCustomerCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.customers.update")]
    public async Task<BillingCustomerInfo> UpdateCustomerAsync(
        Guid tenantId,
        Guid billingCustomerId,
        UpdateBillingCustomerRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => UpdateCustomerCoreAsync(tenantId, billingCustomerId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = billingCustomerId },
            ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<BillingCustomerInfo>> ListCustomersCoreAsync(Guid tenantId, CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        var records = await _store.ListCustomersAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(BillingCustomerInfo.FromRecord).ToList();
    }

    private async Task<BillingCustomerInfo?> GetCustomerCoreAsync(Guid tenantId, Guid billingCustomerId, CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        BillingServiceValidation.ValidateId(billingCustomerId, nameof(billingCustomerId));
        var record = await _store.GetCustomerAsync(tenantId, billingCustomerId, ct).ConfigureAwait(false);
        return record is null ? null : BillingCustomerInfo.FromRecord(record);
    }

    private async Task<BillingCustomerInfo> CreateCustomerCoreAsync(
        Guid tenantId,
        CreateBillingCustomerRequest request,
        CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var now = DateTimeOffset.UtcNow;
        var record = new BillingCustomerRecord(
            BillingCustomerId: Guid.NewGuid(),
            TenantId: tenantId,
            UserId: request.UserId == Guid.Empty ? null : request.UserId,
            DisplayName: BillingServiceValidation.Required(request.DisplayName, nameof(request.DisplayName)),
            Email: BillingPriceReferenceInfo.NormalizeOptional(request.Email),
            BillingMode: BillingModes.Normalize(request.BillingMode),
            Status: BillingCustomerStatuses.Normalize(request.Status ?? BillingCustomerStatuses.Active),
            ProviderName: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderName),
            ProviderCustomerId: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderCustomerId),
            DefaultPaymentMethod: request.DefaultPaymentMethod,
            CreatedUtc: now,
            UpdatedUtc: now,
            Metadata: BillingPriceReferenceInfo.NormalizeMetadata(request.Metadata));

        var saved = await _store.SaveCustomerAsync(record, ct).ConfigureAwait(false);
        return BillingCustomerInfo.FromRecord(saved);
    }

    private async Task<BillingCustomerInfo> UpdateCustomerCoreAsync(
        Guid tenantId,
        Guid billingCustomerId,
        UpdateBillingCustomerRequest request,
        CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        BillingServiceValidation.ValidateId(billingCustomerId, nameof(billingCustomerId));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var existing = await GetRequiredAsync(tenantId, billingCustomerId, ct).ConfigureAwait(false);
        var updated = existing with
        {
            DisplayName = BillingServiceValidation.RequiredOrExisting(request.DisplayName, existing.DisplayName),
            Email = BillingServiceValidation.OptionalOrExisting(request.Email, existing.Email),
            BillingMode = string.IsNullOrWhiteSpace(request.BillingMode) ? existing.BillingMode : BillingModes.Normalize(request.BillingMode),
            Status = string.IsNullOrWhiteSpace(request.Status) ? existing.Status : BillingCustomerStatuses.Normalize(request.Status),
            ProviderName = BillingServiceValidation.OptionalOrExisting(request.ProviderName, existing.ProviderName),
            ProviderCustomerId = BillingServiceValidation.OptionalOrExisting(request.ProviderCustomerId, existing.ProviderCustomerId),
            DefaultPaymentMethod = request.DefaultPaymentMethod ?? existing.DefaultPaymentMethod,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Metadata = BillingServiceValidation.MetadataOrExisting(request.Metadata, existing.Metadata)
        };

        var saved = await _store.SaveCustomerAsync(updated, ct).ConfigureAwait(false);
        return BillingCustomerInfo.FromRecord(saved);
    }

    private async Task<BillingCustomerRecord> GetRequiredAsync(Guid tenantId, Guid billingCustomerId, CancellationToken ct)
    {
        var existing = await _store.GetCustomerAsync(tenantId, billingCustomerId, ct).ConfigureAwait(false);
        return existing ?? throw new BillingException($"Billing customer '{billingCustomerId}' was not found for tenant '{tenantId}'.");
    }
}
