using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Billing;
using IBeam.Credits;
using IBeam.Licensing;
using Microsoft.Extensions.Options;

namespace IBeam.Commerce.Repositories.AzureTable;

public sealed class AzureTableCommerceStore : ILicensingStore, IBillingStore, ICreditReservationStore
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
        if (existing is not null)
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
