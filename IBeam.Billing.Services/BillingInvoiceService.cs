using IBeam.Services.Abstractions;

namespace IBeam.Billing.Services;

[IBeamOperation("billing.invoices")]
public sealed class BillingInvoiceService : IBillingInvoiceService
{
    private readonly IBillingStore _store;
    private readonly IServiceOperationExecutor _operations;

    public BillingInvoiceService(IBillingStore store, IServiceOperationExecutor? operations = null)
    {
        _store = store;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("billing.invoices.list")]
    public async Task<IReadOnlyList<BillingInvoiceInfo>> ListInvoicesAsync(Guid tenantId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListInvoicesCoreAsync(tenantId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.invoices.get")]
    public async Task<BillingInvoiceInfo?> GetInvoiceAsync(Guid tenantId, Guid billingInvoiceId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GetInvoiceCoreAsync(tenantId, billingInvoiceId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = billingInvoiceId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.invoices.create")]
    public async Task<BillingInvoiceInfo> CreateInvoiceAsync(Guid tenantId, CreateBillingInvoiceRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => CreateInvoiceCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.BillingCustomerId },
            ct).ConfigureAwait(false);

    [IBeamOperation("billing.invoices.update")]
    public async Task<BillingInvoiceInfo> UpdateInvoiceAsync(
        Guid tenantId,
        Guid billingInvoiceId,
        UpdateBillingInvoiceRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => UpdateInvoiceCoreAsync(tenantId, billingInvoiceId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = billingInvoiceId },
            ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<BillingInvoiceInfo>> ListInvoicesCoreAsync(Guid tenantId, CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        var records = await _store.ListInvoicesAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(BillingInvoiceInfo.FromRecord).ToList();
    }

    private async Task<BillingInvoiceInfo?> GetInvoiceCoreAsync(Guid tenantId, Guid billingInvoiceId, CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        BillingServiceValidation.ValidateId(billingInvoiceId, nameof(billingInvoiceId));
        var record = await _store.GetInvoiceAsync(tenantId, billingInvoiceId, ct).ConfigureAwait(false);
        return record is null ? null : BillingInvoiceInfo.FromRecord(record);
    }

    private async Task<BillingInvoiceInfo> CreateInvoiceCoreAsync(Guid tenantId, CreateBillingInvoiceRequest request, CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        BillingServiceValidation.ValidateId(request.BillingCustomerId, nameof(request.BillingCustomerId));
        var now = DateTimeOffset.UtcNow;
        var record = new BillingInvoiceRecord(
            BillingInvoiceId: Guid.NewGuid(),
            TenantId: tenantId,
            UserId: request.UserId == Guid.Empty ? null : request.UserId,
            BillingCustomerId: request.BillingCustomerId,
            BillingSubscriptionId: request.BillingSubscriptionId == Guid.Empty ? null : request.BillingSubscriptionId,
            BillingMode: BillingModes.Normalize(request.BillingMode),
            Status: BillingInvoiceStatuses.Normalize(request.Status ?? BillingInvoiceStatuses.Open),
            ProviderName: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderName),
            ProviderInvoiceId: BillingPriceReferenceInfo.NormalizeOptional(request.ProviderInvoiceId),
            InvoiceNumber: BillingPriceReferenceInfo.NormalizeOptional(request.InvoiceNumber),
            Currency: BillingPriceReferenceInfo.NormalizeCurrency(request.Currency) ?? "USD",
            AmountDue: request.AmountDue,
            AmountPaid: request.AmountPaid,
            DueUtc: request.DueUtc,
            PaidUtc: request.PaidUtc,
            HostedInvoiceUrl: BillingPriceReferenceInfo.NormalizeOptional(request.HostedInvoiceUrl),
            CreatedUtc: now,
            UpdatedUtc: now,
            Metadata: BillingPriceReferenceInfo.NormalizeMetadata(request.Metadata));

        var saved = await _store.SaveInvoiceAsync(record, ct).ConfigureAwait(false);
        return BillingInvoiceInfo.FromRecord(saved);
    }

    private async Task<BillingInvoiceInfo> UpdateInvoiceCoreAsync(
        Guid tenantId,
        Guid billingInvoiceId,
        UpdateBillingInvoiceRequest request,
        CancellationToken ct)
    {
        BillingServiceValidation.ValidateTenantId(tenantId);
        BillingServiceValidation.ValidateId(billingInvoiceId, nameof(billingInvoiceId));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var existing = await GetRequiredAsync(tenantId, billingInvoiceId, ct).ConfigureAwait(false);
        var updated = existing with
        {
            BillingMode = string.IsNullOrWhiteSpace(request.BillingMode) ? existing.BillingMode : BillingModes.Normalize(request.BillingMode),
            Status = string.IsNullOrWhiteSpace(request.Status) ? existing.Status : BillingInvoiceStatuses.Normalize(request.Status),
            ProviderName = BillingServiceValidation.OptionalOrExisting(request.ProviderName, existing.ProviderName),
            ProviderInvoiceId = BillingServiceValidation.OptionalOrExisting(request.ProviderInvoiceId, existing.ProviderInvoiceId),
            InvoiceNumber = BillingServiceValidation.OptionalOrExisting(request.InvoiceNumber, existing.InvoiceNumber),
            Currency = BillingPriceReferenceInfo.NormalizeCurrency(request.Currency) ?? existing.Currency,
            AmountDue = request.AmountDue ?? existing.AmountDue,
            AmountPaid = request.AmountPaid ?? existing.AmountPaid,
            DueUtc = request.DueUtc ?? existing.DueUtc,
            PaidUtc = request.PaidUtc ?? existing.PaidUtc,
            HostedInvoiceUrl = BillingServiceValidation.OptionalOrExisting(request.HostedInvoiceUrl, existing.HostedInvoiceUrl),
            UpdatedUtc = DateTimeOffset.UtcNow,
            Metadata = BillingServiceValidation.MetadataOrExisting(request.Metadata, existing.Metadata)
        };

        var saved = await _store.SaveInvoiceAsync(updated, ct).ConfigureAwait(false);
        return BillingInvoiceInfo.FromRecord(saved);
    }

    private async Task<BillingInvoiceRecord> GetRequiredAsync(Guid tenantId, Guid billingInvoiceId, CancellationToken ct)
    {
        var existing = await _store.GetInvoiceAsync(tenantId, billingInvoiceId, ct).ConfigureAwait(false);
        return existing ?? throw new BillingException($"Billing invoice '{billingInvoiceId}' was not found for tenant '{tenantId}'.");
    }
}
