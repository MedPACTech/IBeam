using System.Collections.Concurrent;

namespace IBeam.Credits.Services;

public sealed class InMemoryCreditStore : ICreditReservationStore
{
    private readonly ConcurrentDictionary<Guid, CreditLedgerEntryInfo> _ledger = [];
    private readonly ConcurrentDictionary<(Guid TenantId, Guid ReservationId), CreditReservationInfo> _reservations = [];
    private readonly ConcurrentDictionary<(Guid TenantId, string IdempotencyKey), Guid> _reservationIdsByIdempotencyKey = [];

    public Task AppendLedgerEntryAsync(CreditLedgerEntryInfo entry, CancellationToken ct = default)
    {
        _ledger[entry.CreditLedgerEntryId] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CreditLedgerEntryInfo>> ListLedgerEntriesAsync(
        Guid tenantId,
        Guid creditAccountId,
        string? bucketKey = null,
        CancellationToken ct = default)
    {
        var normalizedBucket = CreditNormalization.NormalizeOptional(bucketKey);
        return Task.FromResult<IReadOnlyList<CreditLedgerEntryInfo>>(
            _ledger.Values
                .Where(x => x.TenantId == tenantId &&
                            x.CreditAccountId == creditAccountId &&
                            (normalizedBucket is null || string.Equals(x.BucketKey, CreditNormalization.NormalizeKey(normalizedBucket, nameof(bucketKey)), StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.EffectiveUtc)
                .ToList());
    }

    public Task<CreditReservationInfo> SaveReservationAsync(CreditReservationInfo reservation, CancellationToken ct = default)
    {
        _reservations[(reservation.TenantId, reservation.CreditReservationId)] = reservation;
        if (!string.IsNullOrWhiteSpace(reservation.IdempotencyKey))
            _reservationIdsByIdempotencyKey.TryAdd((reservation.TenantId, reservation.IdempotencyKey), reservation.CreditReservationId);
        return Task.FromResult(reservation);
    }

    public Task<CreditReservationInfo?> GetReservationAsync(Guid tenantId, Guid creditReservationId, CancellationToken ct = default)
    {
        _reservations.TryGetValue((tenantId, creditReservationId), out var reservation);
        return Task.FromResult(reservation);
    }

    public Task<CreditReservationInfo?> GetReservationByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct = default)
    {
        if (_reservationIdsByIdempotencyKey.TryGetValue((tenantId, idempotencyKey), out var reservationId) &&
            _reservations.TryGetValue((tenantId, reservationId), out var reservation))
        {
            return Task.FromResult<CreditReservationInfo?>(reservation);
        }

        return Task.FromResult<CreditReservationInfo?>(null);
    }

    public Task<IReadOnlyList<CreditReservationInfo>> ListReservationsAsync(Guid tenantId, Guid? creditAccountId = null, string? bucketKey = null, CancellationToken ct = default)
    {
        var normalizedBucket = CreditNormalization.NormalizeOptional(bucketKey);
        return Task.FromResult<IReadOnlyList<CreditReservationInfo>>(
            _reservations.Values
                .Where(x => x.TenantId == tenantId &&
                            (creditAccountId is null || x.CreditAccountId == creditAccountId) &&
                            (normalizedBucket is null || string.Equals(x.BucketKey, CreditNormalization.NormalizeKey(normalizedBucket, nameof(bucketKey)), StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.CreatedUtc)
                .ToList());
    }
}
