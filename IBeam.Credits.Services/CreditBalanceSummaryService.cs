using IBeam.Services.Abstractions;

namespace IBeam.Credits.Services;

[IBeamOperation("credits.summaries")]
public sealed class CreditBalanceSummaryService : ICreditBalanceSummaryService
{
    private readonly ICreditReservationStore _store;
    private readonly IServiceOperationExecutor _operations;

    public CreditBalanceSummaryService(ICreditReservationStore store, IServiceOperationExecutor? operations = null)
    {
        _store = store;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("credits.summaries.runtime")]
    public async Task<CreditRuntimeSummaryInfo> GetRuntimeSummaryAsync(Guid tenantId, GetCreditRuntimeSummaryRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GetRuntimeSummaryCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    [IBeamOperation("credits.ledger.list")]
    public async Task<IReadOnlyList<CreditLedgerEntryInfo>> ListLedgerEntriesAsync(Guid tenantId, ListCreditLedgerEntriesRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListLedgerEntriesCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    [IBeamOperation("credits.reservations.list")]
    public async Task<IReadOnlyList<CreditReservationInfo>> ListReservationsAsync(Guid tenantId, ListCreditReservationsRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListReservationsCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    private async Task<CreditRuntimeSummaryInfo> GetRuntimeSummaryCoreAsync(Guid tenantId, GetCreditRuntimeSummaryRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateAccountId(request.CreditAccountId);

        var bucketKeys = request.BucketKeys
            .Select(x => CreditNormalization.NormalizeKey(x, nameof(request.BucketKeys)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (bucketKeys.Count == 0)
            throw new CreditException("At least one bucket key is required.");

        var asOf = request.AsOfUtc ?? DateTimeOffset.UtcNow;
        var balances = new List<CreditBucketBalanceSummaryInfo>();
        foreach (var bucketKey in bucketKeys)
        {
            var entries = await _store.ListLedgerEntriesAsync(tenantId, request.CreditAccountId, bucketKey, ct).ConfigureAwait(false);
            var balance = CreditLedgerCalculator.CalculateBalance(tenantId, request.CreditAccountId, bucketKey, entries, asOf);
            var reservations = await _store.ListReservationsAsync(tenantId, request.CreditAccountId, bucketKey, ct).ConfigureAwait(false);
            var activeReserved = reservations
                .Where(x => x.Status == CreditReservationStatuses.Active && x.ExpiresUtc > asOf)
                .Sum(x => x.ReservedAmount);

            balances.Add(new CreditBucketBalanceSummaryInfo(
                bucketKey,
                balance.Granted,
                balance.Debited,
                balance.Expired,
                balance.Available,
                activeReserved,
                Math.Max(0, balance.Available - activeReserved),
                asOf));
        }

        return new CreditRuntimeSummaryInfo(tenantId, request.CreditAccountId, true, asOf, balances);
    }

    private async Task<IReadOnlyList<CreditLedgerEntryInfo>> ListLedgerEntriesCoreAsync(Guid tenantId, ListCreditLedgerEntriesRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateAccountId(request.CreditAccountId);

        var bucketKey = CreditNormalization.NormalizeOptional(request.BucketKey);
        return await _store.ListLedgerEntriesAsync(tenantId, request.CreditAccountId, bucketKey, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CreditReservationInfo>> ListReservationsCoreAsync(Guid tenantId, ListCreditReservationsRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var bucketKey = CreditNormalization.NormalizeOptional(request.BucketKey);
        return await _store.ListReservationsAsync(tenantId, request.CreditAccountId, bucketKey, ct).ConfigureAwait(false);
    }

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new CreditException("tenantId is required.");
    }

    private static void ValidateAccountId(Guid creditAccountId)
    {
        if (creditAccountId == Guid.Empty)
            throw new CreditException("creditAccountId is required.");
    }
}
