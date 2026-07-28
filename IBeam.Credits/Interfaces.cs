namespace IBeam.Credits;

public interface ICreditLedgerStore
{
    Task AppendLedgerEntryAsync(CreditLedgerEntryInfo entry, CancellationToken ct = default);

    Task<IReadOnlyList<CreditLedgerEntryInfo>> ListLedgerEntriesAsync(
        Guid tenantId,
        Guid creditAccountId,
        string? bucketKey = null,
        CancellationToken ct = default);
}

public interface ICreditReservationService
{
    Task<CreditReservationInfo> ReserveAsync(Guid tenantId, ReserveCreditsRequest request, CancellationToken ct = default);
    Task<CreditReservationInfo> SettleAsync(Guid tenantId, Guid creditReservationId, SettleCreditReservationRequest request, CancellationToken ct = default);
    Task<CreditReservationInfo> ReleaseAsync(Guid tenantId, Guid creditReservationId, ReleaseCreditReservationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CreditReservationInfo>> ExpireAsync(Guid tenantId, DateTimeOffset? asOfUtc = null, CancellationToken ct = default);
}

public interface ICreditUsageRecorder
{
    Task<CreditUsageRecordResult> RecordUsageAsync(Guid tenantId, RecordCreditUsageRequest request, CancellationToken ct = default);
}

public interface ICreditPolicyService
{
    Task<CreditOperationDecision> BeginOperationAsync(Guid tenantId, BeginCreditOperationRequest request, CancellationToken ct = default);
    Task<CreditOperationSettlementResult> CompleteOperationAsync(Guid tenantId, CompleteCreditOperationRequest request, CancellationToken ct = default);
    Task<CreditOperationSettlementResult> RecordStreamingChunkAsync(Guid tenantId, RecordStreamingCreditChunkRequest request, CancellationToken ct = default);
}

public interface ICreditBalanceSummaryService
{
    Task<CreditRuntimeSummaryInfo> GetRuntimeSummaryAsync(Guid tenantId, GetCreditRuntimeSummaryRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CreditLedgerEntryInfo>> ListLedgerEntriesAsync(Guid tenantId, ListCreditLedgerEntriesRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CreditReservationInfo>> ListReservationsAsync(Guid tenantId, ListCreditReservationsRequest request, CancellationToken ct = default);
}

public interface ICreditReservationStore : ICreditLedgerStore
{
    Task<CreditReservationInfo> SaveReservationAsync(CreditReservationInfo reservation, CancellationToken ct = default);
    Task<CreditReservationInfo?> GetReservationAsync(Guid tenantId, Guid creditReservationId, CancellationToken ct = default);
    Task<CreditReservationInfo?> GetReservationByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<CreditReservationInfo>> ListReservationsAsync(Guid tenantId, Guid? creditAccountId = null, string? bucketKey = null, CancellationToken ct = default);
}
