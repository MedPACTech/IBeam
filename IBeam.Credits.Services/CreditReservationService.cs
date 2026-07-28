using IBeam.Services.Abstractions;

namespace IBeam.Credits.Services;

[IBeamOperation("credits.reservations")]
public sealed class CreditReservationService : ICreditReservationService, ICreditUsageRecorder
{
    private readonly ICreditReservationStore _store;
    private readonly IServiceOperationExecutor _operations;

    public CreditReservationService(ICreditReservationStore store, IServiceOperationExecutor? operations = null)
    {
        _store = store;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("credits.reservations.reserve")]
    public async Task<CreditReservationInfo> ReserveAsync(Guid tenantId, ReserveCreditsRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ReserveCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    [IBeamOperation("credits.reservations.settle")]
    public async Task<CreditReservationInfo> SettleAsync(Guid tenantId, Guid creditReservationId, SettleCreditReservationRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => SettleCoreAsync(tenantId, creditReservationId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = creditReservationId },
            ct).ConfigureAwait(false);

    [IBeamOperation("credits.reservations.release")]
    public async Task<CreditReservationInfo> ReleaseAsync(Guid tenantId, Guid creditReservationId, ReleaseCreditReservationRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ReleaseCoreAsync(tenantId, creditReservationId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = creditReservationId },
            ct).ConfigureAwait(false);

    [IBeamOperation("credits.reservations.expire")]
    public async Task<IReadOnlyList<CreditReservationInfo>> ExpireAsync(Guid tenantId, DateTimeOffset? asOfUtc = null, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ExpireCoreAsync(tenantId, asOfUtc, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    [IBeamOperation("credits.usage.record")]
    public async Task<CreditUsageRecordResult> RecordUsageAsync(Guid tenantId, RecordCreditUsageRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => RecordUsageCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    private async Task<CreditReservationInfo> ReserveCoreAsync(Guid tenantId, ReserveCreditsRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateAccountId(request.CreditAccountId);
        if (request.EstimatedAmount <= 0)
            throw new CreditException("estimatedAmount must be greater than zero.");
        if (request.MaxAmount <= 0 || request.MaxAmount < request.EstimatedAmount)
            throw new CreditException("maxAmount must be greater than or equal to estimatedAmount.");

        var idempotencyKey = CreditNormalization.NormalizeOptional(request.IdempotencyKey);
        if (idempotencyKey is not null)
        {
            var existing = await _store.GetReservationByIdempotencyKeyAsync(tenantId, idempotencyKey, ct).ConfigureAwait(false);
            if (existing is not null)
                return existing;
        }

        var bucketKey = CreditNormalization.NormalizeKey(request.BucketKey, nameof(request.BucketKey));
        var now = DateTimeOffset.UtcNow;
        var expires = request.ExpiresUtc ?? now.AddMinutes(15);
        if (expires <= now)
            throw new CreditException("expiresUtc must be in the future.");

        var available = await GetAvailableAfterActiveReservationsAsync(tenantId, request.CreditAccountId, bucketKey, now, ct).ConfigureAwait(false);
        if (available < request.MaxAmount)
            throw new CreditException("Insufficient available credits for reservation.");

        var reservation = new CreditReservationInfo(
            Guid.NewGuid(),
            tenantId,
            request.CreditAccountId,
            bucketKey,
            request.EstimatedAmount,
            request.MaxAmount,
            request.MaxAmount,
            null,
            CreditReservationStatuses.Active,
            CreditNormalization.NormalizeOptional(request.OperationName),
            idempotencyKey,
            now,
            expires,
            null,
            null,
            null,
            CreditNormalization.NormalizeMetadata(request.Metadata));

        return await _store.SaveReservationAsync(reservation, ct).ConfigureAwait(false);
    }

    private async Task<CreditReservationInfo> SettleCoreAsync(Guid tenantId, Guid creditReservationId, SettleCreditReservationRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.ActualAmount < 0)
            throw new CreditException("actualAmount cannot be negative.");

        var reservation = await GetRequiredReservationAsync(tenantId, creditReservationId, ct).ConfigureAwait(false);
        if (reservation.Status == CreditReservationStatuses.Settled)
            return reservation;
        if (reservation.Status != CreditReservationStatuses.Active)
            throw new CreditException($"Reservation '{creditReservationId}' is not active.");
        if (request.ActualAmount > reservation.MaxAmount)
            throw new CreditException("actualAmount cannot exceed maxAmount for this reservation.");

        var now = DateTimeOffset.UtcNow;
        if (request.ActualAmount > 0)
        {
            var debit = CreditLedgerEntryInfo.CreateDebit(
                tenantId,
                reservation.CreditAccountId,
                reservation.BucketKey,
                request.ActualAmount,
                CreditNormalization.NormalizeOptional(request.OperationName) ?? reservation.OperationName,
                CreditNormalization.NormalizeOptional(request.IdempotencyKey) ?? reservation.IdempotencyKey,
                now,
                MergeMetadata(reservation.Metadata, request.Metadata));
            await _store.AppendLedgerEntryAsync(debit, ct).ConfigureAwait(false);
        }

        var settled = reservation with
        {
            Status = CreditReservationStatuses.Settled,
            ActualAmount = request.ActualAmount,
            SettledUtc = now,
            Metadata = MergeMetadata(reservation.Metadata, request.Metadata)
        };
        return await _store.SaveReservationAsync(settled, ct).ConfigureAwait(false);
    }

    private async Task<CreditReservationInfo> ReleaseCoreAsync(Guid tenantId, Guid creditReservationId, ReleaseCreditReservationRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        var reservation = await GetRequiredReservationAsync(tenantId, creditReservationId, ct).ConfigureAwait(false);
        if (reservation.Status == CreditReservationStatuses.Released)
            return reservation;
        if (reservation.Status != CreditReservationStatuses.Active)
            throw new CreditException($"Reservation '{creditReservationId}' is not active.");

        var metadata = MergeMetadata(reservation.Metadata, request?.Metadata);
        if (!string.IsNullOrWhiteSpace(request?.Reason))
            metadata["releaseReason"] = request.Reason.Trim();

        var released = reservation with
        {
            Status = CreditReservationStatuses.Released,
            ReleasedUtc = DateTimeOffset.UtcNow,
            Metadata = metadata
        };
        return await _store.SaveReservationAsync(released, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CreditReservationInfo>> ExpireCoreAsync(Guid tenantId, DateTimeOffset? asOfUtc, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        var asOf = asOfUtc ?? DateTimeOffset.UtcNow;
        var reservations = await _store.ListReservationsAsync(tenantId, ct: ct).ConfigureAwait(false);
        var expired = new List<CreditReservationInfo>();
        foreach (var reservation in reservations.Where(x => x.Status == CreditReservationStatuses.Active && x.ExpiresUtc <= asOf))
        {
            var updated = reservation with
            {
                Status = CreditReservationStatuses.Expired,
                ExpiredUtc = asOf
            };
            expired.Add(await _store.SaveReservationAsync(updated, ct).ConfigureAwait(false));
        }

        return expired;
    }

    private async Task<CreditUsageRecordResult> RecordUsageCoreAsync(Guid tenantId, RecordCreditUsageRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateAccountId(request.CreditAccountId);
        if (request.Amount <= 0)
            throw new CreditException("amount must be greater than zero.");

        var bucketKey = CreditNormalization.NormalizeKey(request.BucketKey, nameof(request.BucketKey));
        var available = await GetAvailableAfterActiveReservationsAsync(tenantId, request.CreditAccountId, bucketKey, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (available < request.Amount)
            throw new CreditException("Insufficient available credits for usage.");

        var debit = CreditLedgerEntryInfo.CreateDebit(
            tenantId,
            request.CreditAccountId,
            bucketKey,
            request.Amount,
            request.OperationName,
            request.IdempotencyKey,
            DateTimeOffset.UtcNow,
            request.Metadata);
        await _store.AppendLedgerEntryAsync(debit, ct).ConfigureAwait(false);
        var entries = await _store.ListLedgerEntriesAsync(tenantId, request.CreditAccountId, bucketKey, ct).ConfigureAwait(false);
        var balance = CreditLedgerCalculator.CalculateBalance(tenantId, request.CreditAccountId, bucketKey, entries);
        return new CreditUsageRecordResult(debit, balance);
    }

    private async Task<decimal> GetAvailableAfterActiveReservationsAsync(Guid tenantId, Guid creditAccountId, string bucketKey, DateTimeOffset asOf, CancellationToken ct)
    {
        var entries = await _store.ListLedgerEntriesAsync(tenantId, creditAccountId, bucketKey, ct).ConfigureAwait(false);
        var balance = CreditLedgerCalculator.CalculateBalance(tenantId, creditAccountId, bucketKey, entries, asOf);
        var reservations = await _store.ListReservationsAsync(tenantId, creditAccountId, bucketKey, ct).ConfigureAwait(false);
        var activeReserved = reservations
            .Where(x => x.Status == CreditReservationStatuses.Active && x.ExpiresUtc > asOf)
            .Sum(x => x.ReservedAmount);
        return Math.Max(0, balance.Available - activeReserved);
    }

    private async Task<CreditReservationInfo> GetRequiredReservationAsync(Guid tenantId, Guid creditReservationId, CancellationToken ct)
    {
        if (creditReservationId == Guid.Empty)
            throw new CreditException("creditReservationId is required.");

        var reservation = await _store.GetReservationAsync(tenantId, creditReservationId, ct).ConfigureAwait(false);
        return reservation ?? throw new CreditException($"Reservation '{creditReservationId}' was not found for tenant '{tenantId}'.");
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

    private static Dictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string>? first,
        IReadOnlyDictionary<string, string>? second)
    {
        var metadata = new Dictionary<string, string>(CreditNormalization.NormalizeMetadata(first), StringComparer.OrdinalIgnoreCase);
        foreach (var item in CreditNormalization.NormalizeMetadata(second))
            metadata[item.Key] = item.Value;
        return metadata;
    }
}
