using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Billing;
using IBeam.Billing.Licensing;
using IBeam.Credits;
using IBeam.Licensing;
using Microsoft.Extensions.Options;

namespace IBeam.Commerce.Repositories.AzureTable;

public sealed class AzureTableCommerceStore :
    ILicensingStore,
    IBillingStore,
    IBillingPurchaseStore,
    IBillingCheckoutAttemptStore,
    IBillingPurchaseClaimStore,
    IBillingSubscriptionProviderBindingStore,
    ICreditReservationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableCommerceOptions _options;

    public AzureTableCommerceStore(
        TableServiceClient serviceClient,
        IOptions<AzureTableCommerceOptions> options)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<IReadOnlyList<TenantLicenseRecord>> ListLicensesAsync(Guid tenantId, CancellationToken ct = default)
        => await ListTenantEntitiesAsync<TenantLicenseRecord>(
            _options.LicensesTableName,
            tenantId,
            ct).ConfigureAwait(false);

    public async Task<TenantLicenseRecord?> GetLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
        => await GetAsync<TenantLicenseRecord>(
            _options.LicensesTableName,
            _options.TenantPk(tenantId),
            _options.LicenseRk(licenseId),
            ct).ConfigureAwait(false);

    public async Task<TenantLicenseRecord> UpsertLicenseAsync(TenantLicenseRecord license, CancellationToken ct = default)
    {
        await UpsertAsync(
            _options.LicensesTableName,
            _options.TenantPk(license.TenantId),
            _options.LicenseRk(license.LicenseId),
            license,
            license.TenantId,
            license.LicenseId,
            license.Status,
            ct: ct).ConfigureAwait(false);
        return license;
    }

    public async Task DeleteLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
    {
        await DeleteAsync(_options.LicensesTableName, _options.TenantPk(tenantId), _options.LicenseRk(licenseId), ct).ConfigureAwait(false);
        var assignments = await ListAssignmentsAsync(tenantId, licenseId, ct).ConfigureAwait(false);
        foreach (var assignment in assignments)
        {
            await DeleteAssignmentAsync(tenantId, licenseId, assignment.AssignmentId, ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
    {
        var table = await GetTableAsync(_options.LicenseAssignmentsTableName, ct).ConfigureAwait(false);
        var partitionKey = _options.TenantPk(tenantId);
        var rowPrefix = $"LIC|{licenseId:D}|ASN|";
        var results = new List<LicenseSeatAssignmentInfo>();
        await foreach (var entity in table.QueryAsync<AzureTableJsonEntity>(
                           x => x.PartitionKey == partitionKey,
                           cancellationToken: ct).ConfigureAwait(false))
        {
            if (entity.RowKey.StartsWith(rowPrefix, StringComparison.OrdinalIgnoreCase))
                results.Add(Deserialize<LicenseSeatAssignmentInfo>(entity.PayloadJson));
        }

        return results.OrderBy(x => x.CreatedUtc).ToList();
    }

    public async Task<LicenseSeatAssignmentInfo> AddAssignmentAsync(LicenseSeatAssignmentInfo assignment, CancellationToken ct = default)
    {
        await UpsertAsync(
            _options.LicenseAssignmentsTableName,
            _options.TenantPk(assignment.TenantId),
            _options.LicenseAssignmentRk(assignment.LicenseId, assignment.AssignmentId),
            assignment,
            assignment.TenantId,
            assignment.AssignmentId,
            ct: ct).ConfigureAwait(false);
        return assignment;
    }

    public Task DeleteAssignmentAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct = default)
        => DeleteAsync(_options.LicenseAssignmentsTableName, _options.TenantPk(tenantId), _options.LicenseAssignmentRk(licenseId, assignmentId), ct);

    public async Task<IReadOnlyList<BillingCustomerRecord>> ListCustomersAsync(Guid tenantId, CancellationToken ct = default)
        => await ListTenantEntitiesAsync<BillingCustomerRecord>(_options.BillingCustomersTableName, tenantId, ct).ConfigureAwait(false);

    public async Task<BillingCustomerRecord?> GetCustomerAsync(Guid tenantId, Guid billingCustomerId, CancellationToken ct = default)
        => await GetAsync<BillingCustomerRecord>(_options.BillingCustomersTableName, _options.TenantPk(tenantId), _options.BillingCustomerRk(billingCustomerId), ct).ConfigureAwait(false);

    public async Task<BillingCustomerRecord> SaveCustomerAsync(BillingCustomerRecord record, CancellationToken ct = default)
    {
        await UpsertAsync(_options.BillingCustomersTableName, _options.TenantPk(record.TenantId), _options.BillingCustomerRk(record.BillingCustomerId), record, record.TenantId, record.BillingCustomerId, record.Status, ct: ct).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<BillingSubscriptionRecord>> ListSubscriptionsAsync(Guid tenantId, CancellationToken ct = default)
        => await ListTenantEntitiesAsync<BillingSubscriptionRecord>(_options.BillingSubscriptionsTableName, tenantId, ct).ConfigureAwait(false);

    public async Task<BillingSubscriptionRecord?> GetSubscriptionAsync(Guid tenantId, Guid billingSubscriptionId, CancellationToken ct = default)
        => await GetAsync<BillingSubscriptionRecord>(_options.BillingSubscriptionsTableName, _options.TenantPk(tenantId), _options.BillingSubscriptionRk(billingSubscriptionId), ct).ConfigureAwait(false);

    public async Task<BillingSubscriptionRecord> SaveSubscriptionAsync(BillingSubscriptionRecord record, CancellationToken ct = default)
    {
        await UpsertAsync(_options.BillingSubscriptionsTableName, _options.TenantPk(record.TenantId), _options.BillingSubscriptionRk(record.BillingSubscriptionId), record, record.TenantId, record.BillingSubscriptionId, record.Status, ct: ct).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<BillingInvoiceRecord>> ListInvoicesAsync(Guid tenantId, CancellationToken ct = default)
        => await ListTenantEntitiesAsync<BillingInvoiceRecord>(_options.BillingInvoicesTableName, tenantId, ct).ConfigureAwait(false);

    public async Task<BillingInvoiceRecord?> GetInvoiceAsync(Guid tenantId, Guid billingInvoiceId, CancellationToken ct = default)
        => await GetAsync<BillingInvoiceRecord>(_options.BillingInvoicesTableName, _options.TenantPk(tenantId), _options.BillingInvoiceRk(billingInvoiceId), ct).ConfigureAwait(false);

    public async Task<BillingInvoiceRecord> SaveInvoiceAsync(BillingInvoiceRecord record, CancellationToken ct = default)
    {
        await UpsertAsync(_options.BillingInvoicesTableName, _options.TenantPk(record.TenantId), _options.BillingInvoiceRk(record.BillingInvoiceId), record, record.TenantId, record.BillingInvoiceId, record.Status, ct: ct).ConfigureAwait(false);
        return record;
    }

    public async Task<BillingProviderEventRecord?> GetProviderEventByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        var entity = await GetEntityAsync(_options.BillingEventsTableName, _options.BillingEventIdempotencyPk(), _options.BillingEventIdempotencyRk(idempotencyKey), ct).ConfigureAwait(false);
        if (entity is null || string.IsNullOrWhiteSpace(entity.PayloadJson))
            return null;

        return await GetAsync<BillingProviderEventRecord>(
            _options.BillingEventsTableName,
            entity.TenantId is { } raw && Guid.TryParse(raw, out var tenantId) ? _options.BillingEventPk(tenantId) : _options.BillingEventPk(null),
            entity.EntityId is { } id && Guid.TryParse(id, out var eventId) ? _options.BillingEventRk(eventId) : string.Empty,
            ct).ConfigureAwait(false);
    }

    public async Task<BillingProviderEventRecord> SaveProviderEventAsync(BillingProviderEventRecord record, CancellationToken ct = default)
    {
        var idempotencyKey = record.IdempotencyKey;
        var existing = await GetProviderEventByIdempotencyKeyAsync(idempotencyKey, ct).ConfigureAwait(false);
        if (existing is not null && existing.BillingProviderEventId != record.BillingProviderEventId)
            return existing;

        await UpsertAsync(_options.BillingEventsTableName, _options.BillingEventPk(record.TenantId), _options.BillingEventRk(record.BillingProviderEventId), record, record.TenantId, record.BillingProviderEventId, record.Status, idempotencyKey, ct).ConfigureAwait(false);
        await AddOrIgnoreAsync(
            _options.BillingEventsTableName,
            new AzureTableJsonEntity
            {
                PartitionKey = _options.BillingEventIdempotencyPk(),
                RowKey = _options.BillingEventIdempotencyRk(idempotencyKey),
                PayloadJson = "{}",
                TenantId = record.TenantId?.ToString("D"),
                EntityId = record.BillingProviderEventId.ToString("D"),
                IdempotencyKey = idempotencyKey
            },
            ct).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<BillingProviderEventRecord>> ListProviderEventsAsync(Guid? tenantId = null, CancellationToken ct = default)
    {
        var table = await GetTableAsync(_options.BillingEventsTableName, ct).ConfigureAwait(false);
        var results = new List<BillingProviderEventRecord>();
        if (tenantId is { } id)
        {
            await foreach (var entity in table.QueryAsync<AzureTableJsonEntity>(
                               x => x.PartitionKey == _options.BillingEventPk(id),
                               cancellationToken: ct).ConfigureAwait(false))
            {
                if (!entity.RowKey.StartsWith("EVT|", StringComparison.OrdinalIgnoreCase))
                    continue;
                results.Add(Deserialize<BillingProviderEventRecord>(entity.PayloadJson));
            }
        }
        else
        {
            await foreach (var entity in table.QueryAsync<AzureTableJsonEntity>(cancellationToken: ct).ConfigureAwait(false))
            {
                if (!entity.RowKey.StartsWith("EVT|", StringComparison.OrdinalIgnoreCase) ||
                    entity.PartitionKey == _options.BillingEventIdempotencyPk())
                {
                    continue;
                }

                results.Add(Deserialize<BillingProviderEventRecord>(entity.PayloadJson));
            }
        }

        return results.OrderByDescending(x => x.ReceivedUtc).ToList();
    }

    public async Task<BillingPurchaseRecord?> GetPurchaseAsync(Guid purchaseId, CancellationToken ct = default)
        => await GetAsync<BillingPurchaseRecord>(
            _options.BillingPurchasesTableName,
            _options.BillingPurchasePk(),
            _options.BillingPurchaseRk(purchaseId),
            ct).ConfigureAwait(false);

    public Task<BillingPurchaseRecord?> GetPurchaseByCorrelationIdAsync(Guid correlationId, CancellationToken ct = default)
        => GetPurchaseByIndexAsync("COR", correlationId.ToString("D"), ct);

    public Task<BillingPurchaseRecord?> GetPurchaseByProviderEventAsync(
        string providerName,
        string providerEventId,
        CancellationToken ct = default)
        => GetPurchaseByIndexAsync("EVT", $"{providerName}:{providerEventId}", ct);

    public Task<BillingPurchaseRecord?> GetPurchaseByLicenseKeyAsync(Guid licenseKey, CancellationToken ct = default)
        => GetPurchaseByIndexAsync("LIC", licenseKey.ToString("D"), ct);

    public async Task<BillingPurchaseRecord> SavePurchaseAsync(
        BillingPurchaseRecord record,
        string? providerEventId = null,
        DateTimeOffset? expectedUpdatedUtc = null,
        CancellationToken ct = default)
    {
        var byCorrelation = await GetPurchaseByCorrelationIdAsync(record.CorrelationId, ct).ConfigureAwait(false);
        if (byCorrelation is not null && byCorrelation.PurchaseId != record.PurchaseId)
            return byCorrelation;
        if (!string.IsNullOrWhiteSpace(providerEventId) && !string.IsNullOrWhiteSpace(record.ProviderName))
        {
            var byEvent = await GetPurchaseByProviderEventAsync(record.ProviderName, providerEventId, ct).ConfigureAwait(false);
            if (byEvent is not null && byEvent.PurchaseId != record.PurchaseId)
                return byEvent;
        }

        var table = await GetTableAsync(_options.BillingPurchasesTableName, ct).ConfigureAwait(false);
        var partitionKey = _options.BillingPurchasePk();
        var rowKey = _options.BillingPurchaseRk(record.PurchaseId);
        var existing = await GetEntityAsync(_options.BillingPurchasesTableName, partitionKey, rowKey, ct).ConfigureAwait(false);
        if (expectedUpdatedUtc is { } expected && existing is not null)
        {
            var current = Deserialize<BillingPurchaseRecord>(existing.PayloadJson);
            if (current.UpdatedUtc != expected)
                throw new BillingException("Purchase changed concurrently; reload it before retrying.");
        }
        var entity = ToEntity(partitionKey, rowKey, record, record.TenantId, record.PurchaseId, record.Status);
        try
        {
            if (existing is null)
                await table.AddEntityAsync(entity, ct).ConfigureAwait(false);
            else
                await table.UpdateEntityAsync(entity, existing.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            var current = await GetPurchaseAsync(record.PurchaseId, ct).ConfigureAwait(false);
            if (current is not null && current.UpdatedUtc >= record.UpdatedUtc)
                return current;
            throw new BillingException("Purchase changed concurrently; reload it before retrying.");
        }

        await SavePurchaseIndexAsync("COR", record.CorrelationId.ToString("D"), record.PurchaseId, ct).ConfigureAwait(false);
        if (record.LicenseKey is { } licenseKey)
            await SavePurchaseIndexAsync("LIC", licenseKey.ToString("D"), record.PurchaseId, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(providerEventId) && !string.IsNullOrWhiteSpace(record.ProviderName))
            await SavePurchaseIndexAsync("EVT", $"{record.ProviderName}:{providerEventId}", record.PurchaseId, ct).ConfigureAwait(false);

        var canonicalIndex = await GetEntityAsync(
            _options.BillingPurchasesTableName,
            _options.BillingPurchaseIndexPk(),
            _options.BillingPurchaseIndexRk("COR", record.CorrelationId.ToString("D")),
            ct).ConfigureAwait(false);
        if (canonicalIndex is not null &&
            Guid.TryParse(canonicalIndex.EntityId, out var canonicalId) &&
            canonicalId != record.PurchaseId)
        {
            await DeleteAsync(_options.BillingPurchasesTableName, partitionKey, rowKey, ct).ConfigureAwait(false);
            return await GetPurchaseAsync(canonicalId, ct).ConfigureAwait(false)
                   ?? throw new BillingException("The canonical purchase is still being created; retry the request.");
        }
        return record;
    }

    public async Task<int> DeleteExpiredPurchasesAsync(DateTimeOffset cutoffUtc, int maxCount = 100, CancellationToken ct = default)
    {
        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount));
        var table = await GetTableAsync(_options.BillingPurchasesTableName, ct).ConfigureAwait(false);
        var expired = new List<BillingPurchaseRecord>();
        await foreach (var entity in table.QueryAsync<AzureTableJsonEntity>(
                           x => x.PartitionKey == _options.BillingPurchasePk(),
                           cancellationToken: ct).ConfigureAwait(false))
        {
            if (!entity.RowKey.StartsWith("PUR|", StringComparison.OrdinalIgnoreCase))
                continue;
            var purchase = Deserialize<BillingPurchaseRecord>(entity.PayloadJson);
            if (purchase.ExpiresUtc <= cutoffUtc &&
                purchase.Status is BillingPurchaseStatuses.Initiated or BillingPurchaseStatuses.AwaitingPayment or BillingPurchaseStatuses.Expired)
            {
                expired.Add(purchase);
                if (expired.Count == maxCount)
                    break;
            }
        }

        foreach (var purchase in expired)
        {
            await DeleteAsync(_options.BillingPurchasesTableName, _options.BillingPurchasePk(), _options.BillingPurchaseRk(purchase.PurchaseId), ct).ConfigureAwait(false);
            await DeleteAsync(_options.BillingPurchasesTableName, _options.BillingPurchaseIndexPk(), _options.BillingPurchaseIndexRk("COR", purchase.CorrelationId.ToString("D")), ct).ConfigureAwait(false);
            if (purchase.LicenseKey is { } licenseKey)
                await DeleteAsync(_options.BillingPurchasesTableName, _options.BillingPurchaseIndexPk(), _options.BillingPurchaseIndexRk("LIC", licenseKey.ToString("D")), ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(purchase.ProviderName) && !string.IsNullOrWhiteSpace(purchase.LastProviderEventId))
                await DeleteAsync(_options.BillingPurchasesTableName, _options.BillingPurchaseIndexPk(), _options.BillingPurchaseIndexRk("EVT", $"{purchase.ProviderName}:{purchase.LastProviderEventId}"), ct).ConfigureAwait(false);
        }

        return expired.Count;
    }

    public async Task<IReadOnlyList<BillingCheckoutAttemptInfo>> ListAttemptsAsync(Guid purchaseId, CancellationToken ct = default)
    {
        var table = await GetTableAsync(_options.BillingCheckoutAttemptsTableName, ct).ConfigureAwait(false);
        var results = new List<BillingCheckoutAttemptInfo>();
        await foreach (var entity in table.QueryAsync<AzureTableJsonEntity>(
                           x => x.PartitionKey == _options.BillingCheckoutAttemptPk(purchaseId),
                           cancellationToken: ct).ConfigureAwait(false))
        {
            results.Add(Deserialize<BillingCheckoutAttemptInfo>(entity.PayloadJson));
        }

        return results.OrderBy(x => x.CreatedUtc).ToList();
    }

    public async Task<BillingCheckoutAttemptInfo> SaveAttemptAsync(BillingCheckoutAttemptInfo attempt, CancellationToken ct = default)
    {
        var table = await GetTableAsync(_options.BillingCheckoutAttemptsTableName, ct).ConfigureAwait(false);
        var entity = ToEntity(
            _options.BillingCheckoutAttemptPk(attempt.PurchaseId),
            _options.BillingCheckoutAttemptRk(attempt.CheckoutAttemptId),
            attempt,
            null,
            attempt.CheckoutAttemptId,
            attempt.Status);
        try
        {
            await table.AddEntityAsync(entity, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
        }
        return attempt;
    }

    public async Task<BillingPurchaseClaimRecord?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => await GetClaimByIndexAsync("TOK", tokenHash, ct).ConfigureAwait(false);

    public async Task<BillingPurchaseClaimRecord?> GetByPurchaseAsync(Guid purchaseId, CancellationToken ct = default)
        => await GetClaimByIndexAsync("PUR", purchaseId.ToString("D"), ct).ConfigureAwait(false);

    public async Task<BillingPurchaseClaimRecord> SaveIssuedAsync(BillingPurchaseClaimRecord record, CancellationToken ct = default)
    {
        var existing = await GetByPurchaseAsync(record.PurchaseId, ct).ConfigureAwait(false);
        if (existing?.ClaimedUtc is not null)
            throw new BillingException("Purchase has already been claimed.");
        var table = await GetTableAsync(_options.BillingPurchaseClaimsTableName, ct).ConfigureAwait(false);
        var actions = new List<TableTransactionAction>();
        var purchaseIndexRow = _options.BillingClaimIndexRk("PUR", record.PurchaseId.ToString("D"));
        var purchaseIndex = await GetEntityAsync(_options.BillingPurchaseClaimsTableName, _options.BillingClaimIndexPk(), purchaseIndexRow, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var existingClaimEntity = await GetEntityAsync(_options.BillingPurchaseClaimsTableName, _options.BillingClaimPk(), _options.BillingClaimRk(existing.ClaimId), ct).ConfigureAwait(false);
            var existingTokenIndex = await GetEntityAsync(_options.BillingPurchaseClaimsTableName, _options.BillingClaimIndexPk(), _options.BillingClaimIndexRk("TOK", existing.TokenHash), ct).ConfigureAwait(false);
            if (existingClaimEntity is not null)
                actions.Add(new TableTransactionAction(TableTransactionActionType.Delete, existingClaimEntity, existingClaimEntity.ETag));
            if (existingTokenIndex is not null)
                actions.Add(new TableTransactionAction(TableTransactionActionType.Delete, existingTokenIndex, existingTokenIndex.ETag));
        }

        actions.Add(new TableTransactionAction(
            TableTransactionActionType.Add,
            ToEntity(_options.BillingClaimPk(), _options.BillingClaimRk(record.ClaimId), record, null, record.ClaimId, "issued")));
        actions.Add(new TableTransactionAction(
            TableTransactionActionType.Add,
            ClaimIndexEntity("TOK", record.TokenHash, record.ClaimId)));
        var nextPurchaseIndex = ClaimIndexEntity("PUR", record.PurchaseId.ToString("D"), record.ClaimId);
        if (purchaseIndex is null)
            actions.Add(new TableTransactionAction(TableTransactionActionType.Add, nextPurchaseIndex));
        else
            actions.Add(new TableTransactionAction(TableTransactionActionType.UpdateReplace, nextPurchaseIndex, purchaseIndex.ETag));

        try
        {
            await table.SubmitTransactionAsync(actions, ct).ConfigureAwait(false);
            return record;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            throw new BillingException("Purchase claim changed concurrently; issue a new claim token and retry.");
        }
    }

    public async Task<BillingPurchaseClaimRecord?> TryClaimAsync(
        string tokenHash,
        Guid tenantId,
        Guid userId,
        DateTimeOffset claimedUtc,
        CancellationToken ct = default)
    {
        var index = await GetEntityAsync(
            _options.BillingPurchaseClaimsTableName,
            _options.BillingClaimIndexPk(),
            _options.BillingClaimIndexRk("TOK", tokenHash),
            ct).ConfigureAwait(false);
        if (index is null || !Guid.TryParse(index.EntityId, out var claimId))
            return null;

        var table = await GetTableAsync(_options.BillingPurchaseClaimsTableName, ct).ConfigureAwait(false);
        var entity = await GetEntityAsync(_options.BillingPurchaseClaimsTableName, _options.BillingClaimPk(), _options.BillingClaimRk(claimId), ct).ConfigureAwait(false);
        if (entity is null)
            return null;
        var claim = Deserialize<BillingPurchaseClaimRecord>(entity.PayloadJson);
        if (claim.ClaimedUtc is not null)
            return claim;

        var claimed = claim with { ClaimedTenantId = tenantId, ClaimedUserId = userId, ClaimedUtc = claimedUtc };
        var updatedEntity = ToEntity(entity.PartitionKey, entity.RowKey, claimed, tenantId, claimId, "claimed");
        try
        {
            await table.UpdateEntityAsync(updatedEntity, entity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            return claimed;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return await GetByTokenHashAsync(tokenHash, ct).ConfigureAwait(false);
        }
    }

    public async Task<BillingProviderMigrationRecord?> GetMigrationAsync(
        Guid tenantId,
        Guid billingSubscriptionId,
        string targetProviderName,
        string idempotencyKey,
        CancellationToken ct = default)
        => await GetAsync<BillingProviderMigrationRecord>(
            _options.BillingProviderBindingsTableName,
            _options.BillingProviderBindingPk(tenantId),
            _options.BillingProviderMigrationRk(billingSubscriptionId, targetProviderName, idempotencyKey),
            ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<BillingSubscriptionProviderBindingInfo>> ListBindingsAsync(
        Guid tenantId,
        Guid billingSubscriptionId,
        CancellationToken ct = default)
    {
        var table = await GetTableAsync(_options.BillingProviderBindingsTableName, ct).ConfigureAwait(false);
        var prefix = $"SUB|{billingSubscriptionId:D}|BND|";
        var results = new List<BillingSubscriptionProviderBindingInfo>();
        await foreach (var entity in table.QueryAsync<AzureTableJsonEntity>(
                           x => x.PartitionKey == _options.BillingProviderBindingPk(tenantId),
                           cancellationToken: ct).ConfigureAwait(false))
        {
            if (entity.RowKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                results.Add(Deserialize<BillingSubscriptionProviderBindingInfo>(entity.PayloadJson));
        }
        return results.OrderBy(x => x.ActivatedUtc).ToList();
    }

    public async Task<BillingProviderMigrationRecord> CommitMigrationAsync(
        BillingProviderMigrationRecord migration,
        BillingSubscriptionProviderBindingInfo sourceBinding,
        BillingSubscriptionProviderBindingInfo targetBinding,
        CancellationToken ct = default)
    {
        var existing = await GetMigrationAsync(
            migration.TenantId,
            migration.BillingSubscriptionId,
            migration.TargetProviderName,
            migration.IdempotencyKey,
            ct).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var table = await GetTableAsync(_options.BillingProviderBindingsTableName, ct).ConfigureAwait(false);
        var partitionKey = _options.BillingProviderBindingPk(migration.TenantId);
        var currentEntities = new List<AzureTableJsonEntity>();
        await foreach (var entity in table.QueryAsync<AzureTableJsonEntity>(
                           x => x.PartitionKey == partitionKey,
                           cancellationToken: ct).ConfigureAwait(false))
        {
            if (entity.RowKey.StartsWith($"SUB|{migration.BillingSubscriptionId:D}|BND|", StringComparison.OrdinalIgnoreCase))
                currentEntities.Add(entity);
        }

        var actions = new List<TableTransactionAction>();
        foreach (var entity in currentEntities)
        {
            var binding = Deserialize<BillingSubscriptionProviderBindingInfo>(entity.PayloadJson);
            if (!binding.IsActive)
                continue;
            var retired = binding with { IsActive = false, RetiredUtc = migration.CompletedUtc };
            var retiredEntity = ToEntity(entity.PartitionKey, entity.RowKey, retired, migration.TenantId, binding.BindingId, "retired");
            retiredEntity.ETag = entity.ETag;
            actions.Add(new TableTransactionAction(TableTransactionActionType.UpdateReplace, retiredEntity, entity.ETag));
        }

        var sourceExists = currentEntities.Any(entity =>
        {
            var binding = Deserialize<BillingSubscriptionProviderBindingInfo>(entity.PayloadJson);
            return string.Equals(binding.ProviderName, sourceBinding.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(binding.ProviderSubscriptionId, sourceBinding.ProviderSubscriptionId, StringComparison.OrdinalIgnoreCase);
        });
        if (!sourceExists)
        {
            actions.Add(new TableTransactionAction(
                TableTransactionActionType.Add,
                ToEntity(partitionKey, _options.BillingProviderBindingRk(sourceBinding.BillingSubscriptionId, sourceBinding.BindingId), sourceBinding, migration.TenantId, sourceBinding.BindingId, "retired")));
        }
        actions.Add(new TableTransactionAction(
            TableTransactionActionType.Add,
            ToEntity(partitionKey, _options.BillingProviderBindingRk(targetBinding.BillingSubscriptionId, targetBinding.BindingId), targetBinding, migration.TenantId, targetBinding.BindingId, "active")));
        actions.Add(new TableTransactionAction(
            TableTransactionActionType.Add,
            ToEntity(partitionKey, _options.BillingProviderMigrationRk(migration.BillingSubscriptionId, migration.TargetProviderName, migration.IdempotencyKey), migration, migration.TenantId, migration.MigrationId, "completed", migration.IdempotencyKey)));

        try
        {
            await table.SubmitTransactionAsync(actions, ct).ConfigureAwait(false);
            return migration;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return await GetMigrationAsync(
                       migration.TenantId,
                       migration.BillingSubscriptionId,
                       migration.TargetProviderName,
                       migration.IdempotencyKey,
                       ct).ConfigureAwait(false)
                   ?? throw new BillingException("Provider migration changed concurrently; retry the operation.");
        }
    }

    public async Task AppendLedgerEntryAsync(CreditLedgerEntryInfo entry, CancellationToken ct = default)
    {
        var table = await GetTableAsync(_options.CreditLedgerTableName, ct).ConfigureAwait(false);
        var entity = ToEntity(_options.TenantPk(entry.TenantId), _options.CreditLedgerRk(entry.CreditLedgerEntryId), entry, entry.TenantId, entry.CreditLedgerEntryId, entry.EntryType, entry.IdempotencyKey, entry.BucketKey);
        try
        {
            await table.AddEntityAsync(entity, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Ledger entries are append-only. Duplicate ids are treated as idempotent writes.
        }
    }

    public async Task<IReadOnlyList<CreditLedgerEntryInfo>> ListLedgerEntriesAsync(Guid tenantId, Guid creditAccountId, string? bucketKey = null, CancellationToken ct = default)
    {
        var records = await ListTenantEntitiesAsync<CreditLedgerEntryInfo>(_options.CreditLedgerTableName, tenantId, ct).ConfigureAwait(false);
        var normalizedBucket = CreditNormalization.NormalizeOptional(bucketKey);
        return records
            .Where(x => x.CreditAccountId == creditAccountId &&
                        (normalizedBucket is null || string.Equals(x.BucketKey, CreditNormalization.NormalizeKey(normalizedBucket, nameof(bucketKey)), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.EffectiveUtc)
            .ToList();
    }

    public async Task<CreditReservationInfo> SaveReservationAsync(CreditReservationInfo reservation, CancellationToken ct = default)
    {
        await UpsertAsync(_options.CreditReservationsTableName, _options.TenantPk(reservation.TenantId), _options.CreditReservationRk(reservation.CreditReservationId), reservation, reservation.TenantId, reservation.CreditReservationId, reservation.Status, reservation.IdempotencyKey, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(reservation.IdempotencyKey))
        {
            await AddOrIgnoreAsync(
                _options.CreditReservationsTableName,
                new AzureTableJsonEntity
                {
                    PartitionKey = _options.CreditReservationIdempotencyPk(reservation.TenantId),
                    RowKey = _options.CreditReservationIdempotencyRk(reservation.IdempotencyKey),
                    PayloadJson = "{}",
                    TenantId = reservation.TenantId.ToString("D"),
                    EntityId = reservation.CreditReservationId.ToString("D"),
                    IdempotencyKey = reservation.IdempotencyKey
                },
                ct).ConfigureAwait(false);
        }

        return reservation;
    }

    public async Task<CreditReservationInfo?> GetReservationAsync(Guid tenantId, Guid creditReservationId, CancellationToken ct = default)
        => await GetAsync<CreditReservationInfo>(_options.CreditReservationsTableName, _options.TenantPk(tenantId), _options.CreditReservationRk(creditReservationId), ct).ConfigureAwait(false);

    public async Task<CreditReservationInfo?> GetReservationByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct = default)
    {
        var entity = await GetEntityAsync(_options.CreditReservationsTableName, _options.CreditReservationIdempotencyPk(tenantId), _options.CreditReservationIdempotencyRk(idempotencyKey), ct).ConfigureAwait(false);
        if (entity is null || !Guid.TryParse(entity.EntityId, out var reservationId))
            return null;

        return await GetReservationAsync(tenantId, reservationId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CreditReservationInfo>> ListReservationsAsync(Guid tenantId, Guid? creditAccountId = null, string? bucketKey = null, CancellationToken ct = default)
    {
        var records = await ListTenantEntitiesAsync<CreditReservationInfo>(_options.CreditReservationsTableName, tenantId, ct).ConfigureAwait(false);
        var normalizedBucket = CreditNormalization.NormalizeOptional(bucketKey);
        return records
            .Where(x => (creditAccountId is null || x.CreditAccountId == creditAccountId) &&
                        (normalizedBucket is null || string.Equals(x.BucketKey, CreditNormalization.NormalizeKey(normalizedBucket, nameof(bucketKey)), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.CreatedUtc)
            .ToList();
    }

    private async Task<BillingPurchaseRecord?> GetPurchaseByIndexAsync(
        string indexType,
        string value,
        CancellationToken ct)
    {
        var index = await GetEntityAsync(
            _options.BillingPurchasesTableName,
            _options.BillingPurchaseIndexPk(),
            _options.BillingPurchaseIndexRk(indexType, value),
            ct).ConfigureAwait(false);
        return index is not null && Guid.TryParse(index.EntityId, out var purchaseId)
            ? await GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false)
            : null;
    }

    private Task SavePurchaseIndexAsync(
        string indexType,
        string value,
        Guid purchaseId,
        CancellationToken ct)
        => AddOrIgnoreAsync(
            _options.BillingPurchasesTableName,
            new AzureTableJsonEntity
            {
                PartitionKey = _options.BillingPurchaseIndexPk(),
                RowKey = _options.BillingPurchaseIndexRk(indexType, value),
                PayloadJson = "{}",
                EntityId = purchaseId.ToString("D")
            },
            ct);

    private async Task<BillingPurchaseClaimRecord?> GetClaimByIndexAsync(
        string indexType,
        string value,
        CancellationToken ct)
    {
        var index = await GetEntityAsync(
            _options.BillingPurchaseClaimsTableName,
            _options.BillingClaimIndexPk(),
            _options.BillingClaimIndexRk(indexType, value),
            ct).ConfigureAwait(false);
        return index is not null && Guid.TryParse(index.EntityId, out var claimId)
            ? await GetAsync<BillingPurchaseClaimRecord>(
                _options.BillingPurchaseClaimsTableName,
                _options.BillingClaimPk(),
                _options.BillingClaimRk(claimId),
                ct).ConfigureAwait(false)
            : null;
    }

    private AzureTableJsonEntity ClaimIndexEntity(string indexType, string value, Guid claimId)
        => new()
        {
            PartitionKey = _options.BillingClaimIndexPk(),
            RowKey = _options.BillingClaimIndexRk(indexType, value),
            PayloadJson = "{}",
            EntityId = claimId.ToString("D")
        };

    private async Task<IReadOnlyList<T>> ListTenantEntitiesAsync<T>(string tableName, Guid tenantId, CancellationToken ct)
    {
        var table = await GetTableAsync(tableName, ct).ConfigureAwait(false);
        var partitionKey = _options.TenantPk(tenantId);
        var results = new List<T>();
        await foreach (var entity in table.QueryAsync<AzureTableJsonEntity>(
                           x => x.PartitionKey == partitionKey,
                           cancellationToken: ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(entity.PayloadJson) || entity.PayloadJson == "{}")
                continue;

            results.Add(Deserialize<T>(entity.PayloadJson));
        }

        return results;
    }

    private async Task<T?> GetAsync<T>(string tableName, string partitionKey, string rowKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rowKey))
            return default;

        var entity = await GetEntityAsync(tableName, partitionKey, rowKey, ct).ConfigureAwait(false);
        return entity is null ? default : Deserialize<T>(entity.PayloadJson);
    }

    private async Task<AzureTableJsonEntity?> GetEntityAsync(string tableName, string partitionKey, string rowKey, CancellationToken ct)
    {
        var table = await GetTableAsync(tableName, ct).ConfigureAwait(false);
        var response = await table.GetEntityIfExistsAsync<AzureTableJsonEntity>(partitionKey, rowKey, cancellationToken: ct).ConfigureAwait(false);
        return response.HasValue ? response.Value : null;
    }

    private async Task UpsertAsync<T>(
        string tableName,
        string partitionKey,
        string rowKey,
        T payload,
        Guid? tenantId,
        Guid entityId,
        string? status = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
        => await UpsertAsync(tableName, partitionKey, rowKey, payload, tenantId, entityId, status, idempotencyKey, null, ct).ConfigureAwait(false);

    private async Task UpsertAsync<T>(
        string tableName,
        string partitionKey,
        string rowKey,
        T payload,
        Guid? tenantId,
        Guid entityId,
        string? status,
        string? idempotencyKey,
        string? bucketKey,
        CancellationToken ct)
    {
        var table = await GetTableAsync(tableName, ct).ConfigureAwait(false);
        await table.UpsertEntityAsync(ToEntity(partitionKey, rowKey, payload, tenantId, entityId, status, idempotencyKey, bucketKey), TableUpdateMode.Replace, ct).ConfigureAwait(false);
    }

    private async Task AddOrIgnoreAsync(string tableName, AzureTableJsonEntity entity, CancellationToken ct)
    {
        var table = await GetTableAsync(tableName, ct).ConfigureAwait(false);
        try
        {
            await table.AddEntityAsync(entity, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
        }
    }

    private async Task DeleteAsync(string tableName, string partitionKey, string rowKey, CancellationToken ct)
    {
        var table = await GetTableAsync(tableName, ct).ConfigureAwait(false);
        try
        {
            await table.DeleteEntityAsync(partitionKey, rowKey, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    private async Task<TableClient> GetTableAsync(string tableName, CancellationToken ct)
    {
        var table = _serviceClient.GetTableClient(_options.FullTableName(tableName));
        if (_options.CreateTablesIfNotExists)
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
        return table;
    }

    private AzureTableJsonEntity ToEntity<T>(
        string partitionKey,
        string rowKey,
        T payload,
        Guid? tenantId,
        Guid entityId,
        string? status = null,
        string? idempotencyKey = null,
        string? bucketKey = null)
        => new()
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            TenantId = tenantId?.ToString("D"),
            EntityId = entityId.ToString("D"),
            BucketKey = bucketKey,
            Status = status,
            IdempotencyKey = idempotencyKey
        };

    private static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidOperationException($"Unable to deserialize {typeof(T).Name}.");
}
