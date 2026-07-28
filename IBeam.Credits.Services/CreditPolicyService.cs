using IBeam.Services.Abstractions;

namespace IBeam.Credits.Services;

[IBeamOperation("credits.policies")]
public sealed class CreditPolicyService : ICreditPolicyService
{
    private readonly ICreditReservationService _reservations;
    private readonly ICreditUsageRecorder _usageRecorder;
    private readonly ICreditLedgerStore _ledgerStore;
    private readonly IServiceOperationExecutor _operations;

    public CreditPolicyService(
        ICreditReservationService reservations,
        ICreditUsageRecorder usageRecorder,
        ICreditLedgerStore ledgerStore,
        IServiceOperationExecutor? operations = null)
    {
        _reservations = reservations;
        _usageRecorder = usageRecorder;
        _ledgerStore = ledgerStore;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("credits.policies.begin")]
    public async Task<CreditOperationDecision> BeginOperationAsync(Guid tenantId, BeginCreditOperationRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => BeginOperationCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    [IBeamOperation("credits.policies.complete")]
    public async Task<CreditOperationSettlementResult> CompleteOperationAsync(Guid tenantId, CompleteCreditOperationRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => CompleteOperationCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    [IBeamOperation("credits.policies.streaming.record-chunk")]
    public async Task<CreditOperationSettlementResult> RecordStreamingChunkAsync(Guid tenantId, RecordStreamingCreditChunkRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => RecordStreamingChunkCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    private async Task<CreditOperationDecision> BeginOperationCoreAsync(Guid tenantId, BeginCreditOperationRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        var mode = CreditPolicyModes.Normalize(request.PolicyMode);
        ValidateAccountId(request.CreditAccountId);
        if (request.EstimatedAmount < 0)
            throw new CreditException("estimatedAmount cannot be negative.");

        var bucketKey = CreditNormalization.NormalizeKey(request.BucketKey, nameof(request.BucketKey));
        if (ExceedsCap(request.EstimatedAmount, request.MaxCredits))
            return CreditOperationDecision.Denied(mode, CreditPolicyDenialReasons.MaxCreditsExceeded, "Estimated credits exceed maxCredits.", request.EstimatedAmount, request.MaxAmount, request.MaxCredits);

        return mode switch
        {
            CreditPolicyModes.FailOpenMetering => CreditOperationDecision.ApprovedWith(mode, request.EstimatedAmount, request.MaxAmount, request.MaxCredits),
            CreditPolicyModes.CapByRequest => await BeginCapByRequestAsync(tenantId, request, bucketKey, mode, ct).ConfigureAwait(false),
            CreditPolicyModes.SoftOverage => await BeginReservedOperationAsync(tenantId, request, bucketKey, mode, request.EstimatedAmount, ct).ConfigureAwait(false),
            CreditPolicyModes.Streaming => await BeginStreamingOperationAsync(tenantId, request, bucketKey, mode, ct).ConfigureAwait(false),
            _ => await BeginReservedOperationAsync(tenantId, request, bucketKey, mode, request.MaxAmount ?? request.MaxCredits ?? request.EstimatedAmount, ct).ConfigureAwait(false)
        };
    }

    private async Task<CreditOperationSettlementResult> CompleteOperationCoreAsync(Guid tenantId, CompleteCreditOperationRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateAccountId(request.CreditAccountId);
        if (request.ActualAmount < 0)
            throw new CreditException("actualAmount cannot be negative.");

        var mode = CreditPolicyModes.Normalize(request.PolicyMode);
        var bucketKey = CreditNormalization.NormalizeKey(request.BucketKey, nameof(request.BucketKey));
        if (mode == CreditPolicyModes.CapByRequest && request.MaxCredits is null)
            return CreditOperationSettlementResult.Denied(mode, CreditPolicyDenialReasons.MaxCreditsRequired, "maxCredits is required for cap-by-request.", request.ActualAmount);
        if (ExceedsCap(request.ActualAmount, request.MaxCredits))
            return CreditOperationSettlementResult.Denied(mode, CreditPolicyDenialReasons.MaxCreditsExceeded, "Actual credits exceed maxCredits.", request.ActualAmount);

        if (mode == CreditPolicyModes.FailOpenMetering)
            return await RecordDirectDebitAsync(tenantId, request.CreditAccountId, bucketKey, mode, request.ActualAmount, request.OperationName, request.IdempotencyKey, request.Metadata, ct).ConfigureAwait(false);

        if (request.CreditReservationId is null)
        {
            if (mode == CreditPolicyModes.SoftOverage && request.AllowOverage)
                return await RecordDirectDebitAsync(tenantId, request.CreditAccountId, bucketKey, mode, request.ActualAmount, request.OperationName, request.IdempotencyKey, request.Metadata, ct).ConfigureAwait(false);

            throw new CreditException("creditReservationId is required for this credit policy mode.");
        }

        var reservationMax = await GetReservationMaxAsync(tenantId, request.CreditReservationId.Value, ct).ConfigureAwait(false);
        var overageAmount = Math.Max(0, request.ActualAmount - reservationMax);
        if (overageAmount > 0 && (mode != CreditPolicyModes.SoftOverage || !request.AllowOverage))
            return CreditOperationSettlementResult.Denied(mode, CreditPolicyDenialReasons.InsufficientCredits, "Actual credits exceed the reserved amount.", request.ActualAmount);

        var reservation = await _reservations.SettleAsync(tenantId, request.CreditReservationId.Value, new SettleCreditReservationRequest
        {
            ActualAmount = Math.Min(request.ActualAmount, reservationMax),
            OperationName = request.OperationName,
            IdempotencyKey = request.IdempotencyKey,
            Metadata = request.Metadata
        }, ct).ConfigureAwait(false);

        CreditLedgerEntryInfo? overageLedger = null;
        if (overageAmount > 0)
        {
            overageLedger = await AppendDebitAsync(tenantId, request.CreditAccountId, bucketKey, overageAmount, request.OperationName, request.IdempotencyKey, AddOverageMetadata(request.Metadata), ct).ConfigureAwait(false);
        }

        var balance = await GetBalanceAsync(tenantId, request.CreditAccountId, bucketKey, ct).ConfigureAwait(false);
        return CreditOperationSettlementResult.ApprovedWith(mode, request.ActualAmount, reservation.ActualAmount.GetValueOrDefault(), overageAmount, balance, reservation, overageLedger);
    }

    private async Task<CreditOperationSettlementResult> RecordStreamingChunkCoreAsync(Guid tenantId, RecordStreamingCreditChunkRequest request, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateAccountId(request.CreditAccountId);
        if (request.ChunkAmount <= 0)
            throw new CreditException("chunkAmount must be greater than zero.");
        if (request.ConsumedToDate < 0)
            throw new CreditException("consumedToDate cannot be negative.");

        var mode = CreditPolicyModes.Streaming;
        var projected = request.ConsumedToDate + request.ChunkAmount;
        if (ExceedsCap(projected, request.MaxCredits))
            return CreditOperationSettlementResult.Denied(mode, CreditPolicyDenialReasons.MaxCreditsExceeded, "Streaming chunk would exceed maxCredits.", request.ChunkAmount);

        var bucketKey = CreditNormalization.NormalizeKey(request.BucketKey, nameof(request.BucketKey));
        if (request.AllowOverage)
            return await RecordDirectDebitAsync(tenantId, request.CreditAccountId, bucketKey, mode, request.ChunkAmount, request.OperationName, request.IdempotencyKey, request.Metadata, ct).ConfigureAwait(false);

        try
        {
            var recorded = await _usageRecorder.RecordUsageAsync(tenantId, new RecordCreditUsageRequest
            {
                CreditAccountId = request.CreditAccountId,
                BucketKey = bucketKey,
                Amount = request.ChunkAmount,
                OperationName = request.OperationName,
                IdempotencyKey = request.IdempotencyKey,
                Metadata = request.Metadata
            }, ct).ConfigureAwait(false);

            return CreditOperationSettlementResult.ApprovedWith(mode, request.ChunkAmount, request.ChunkAmount, 0, recorded.Balance, ledgerEntry: recorded.LedgerEntry);
        }
        catch (CreditException ex) when (ex.Message.Contains("Insufficient available credits", StringComparison.OrdinalIgnoreCase))
        {
            return CreditOperationSettlementResult.Denied(mode, CreditPolicyDenialReasons.InsufficientCredits, ex.Message, request.ChunkAmount);
        }
    }

    private async Task<CreditOperationDecision> BeginCapByRequestAsync(Guid tenantId, BeginCreditOperationRequest request, string bucketKey, string mode, CancellationToken ct)
    {
        if (request.MaxCredits is null)
            return CreditOperationDecision.Denied(mode, CreditPolicyDenialReasons.MaxCreditsRequired, "maxCredits is required for cap-by-request.", request.EstimatedAmount, request.MaxAmount, request.MaxCredits);

        return await BeginReservedOperationAsync(tenantId, request, bucketKey, mode, request.MaxCredits.Value, ct).ConfigureAwait(false);
    }

    private async Task<CreditOperationDecision> BeginStreamingOperationAsync(Guid tenantId, BeginCreditOperationRequest request, string bucketKey, string mode, CancellationToken ct)
    {
        var reservationAmount = request.MaxCredits ?? request.MaxAmount;
        if (reservationAmount is null)
            return CreditOperationDecision.ApprovedWith(mode, request.EstimatedAmount, request.MaxAmount, request.MaxCredits);

        return await BeginReservedOperationAsync(tenantId, request, bucketKey, mode, reservationAmount.Value, ct).ConfigureAwait(false);
    }

    private async Task<CreditOperationDecision> BeginReservedOperationAsync(Guid tenantId, BeginCreditOperationRequest request, string bucketKey, string mode, decimal maxAmount, CancellationToken ct)
    {
        var estimatedAmount = request.EstimatedAmount > 0 ? request.EstimatedAmount : maxAmount;
        if (estimatedAmount <= 0)
            throw new CreditException("estimatedAmount or maxAmount must be greater than zero.");
        if (maxAmount < request.EstimatedAmount)
            return CreditOperationDecision.Denied(mode, CreditPolicyDenialReasons.MaxCreditsExceeded, "maxAmount cannot be less than estimatedAmount.", request.EstimatedAmount, maxAmount, request.MaxCredits);

        try
        {
            var reservation = await _reservations.ReserveAsync(tenantId, new ReserveCreditsRequest
            {
                CreditAccountId = request.CreditAccountId,
                BucketKey = bucketKey,
                EstimatedAmount = estimatedAmount,
                MaxAmount = maxAmount,
                ExpiresUtc = request.ReservationExpiresUtc,
                OperationName = request.OperationName,
                IdempotencyKey = request.IdempotencyKey,
                Metadata = request.Metadata
            }, ct).ConfigureAwait(false);

            return CreditOperationDecision.ApprovedWith(mode, estimatedAmount, maxAmount, request.MaxCredits, reservation);
        }
        catch (CreditException ex) when (ex.Message.Contains("Insufficient available credits", StringComparison.OrdinalIgnoreCase))
        {
            return CreditOperationDecision.Denied(mode, CreditPolicyDenialReasons.InsufficientCredits, ex.Message, request.EstimatedAmount, maxAmount, request.MaxCredits);
        }
    }

    private async Task<CreditOperationSettlementResult> RecordDirectDebitAsync(
        Guid tenantId,
        Guid creditAccountId,
        string bucketKey,
        string mode,
        decimal amount,
        string? operationName,
        string? idempotencyKey,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
    {
        CreditLedgerEntryInfo? ledger = null;
        if (amount > 0)
            ledger = await AppendDebitAsync(tenantId, creditAccountId, bucketKey, amount, operationName, idempotencyKey, metadata, ct).ConfigureAwait(false);

        var balance = await GetBalanceAsync(tenantId, creditAccountId, bucketKey, ct).ConfigureAwait(false);
        var overage = Math.Max(0, balance.Debited - Math.Max(0, balance.Granted - balance.Expired));
        return CreditOperationSettlementResult.ApprovedWith(mode, amount, amount, overage, balance, ledgerEntry: ledger);
    }

    private async Task<CreditLedgerEntryInfo> AppendDebitAsync(
        Guid tenantId,
        Guid creditAccountId,
        string bucketKey,
        decimal amount,
        string? operationName,
        string? idempotencyKey,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
    {
        var debit = CreditLedgerEntryInfo.CreateDebit(tenantId, creditAccountId, bucketKey, amount, operationName, idempotencyKey, DateTimeOffset.UtcNow, metadata);
        await _ledgerStore.AppendLedgerEntryAsync(debit, ct).ConfigureAwait(false);
        return debit;
    }

    private async Task<CreditBalanceInfo> GetBalanceAsync(Guid tenantId, Guid creditAccountId, string bucketKey, CancellationToken ct)
    {
        var entries = await _ledgerStore.ListLedgerEntriesAsync(tenantId, creditAccountId, bucketKey, ct).ConfigureAwait(false);
        return CreditLedgerCalculator.CalculateBalance(tenantId, creditAccountId, bucketKey, entries);
    }

    private async Task<decimal> GetReservationMaxAsync(Guid tenantId, Guid creditReservationId, CancellationToken ct)
    {
        if (_ledgerStore is not ICreditReservationStore reservationStore)
            throw new CreditException("Credit policy service requires an ICreditReservationStore when completing reserved operations.");

        var reservation = await reservationStore.GetReservationAsync(tenantId, creditReservationId, ct).ConfigureAwait(false);
        return reservation?.MaxAmount ?? throw new CreditException($"Reservation '{creditReservationId}' was not found for tenant '{tenantId}'.");
    }

    private static bool ExceedsCap(decimal amount, decimal? maxCredits)
        => maxCredits is { } max && amount > max;

    private static Dictionary<string, string> AddOverageMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        var values = new Dictionary<string, string>(CreditNormalization.NormalizeMetadata(metadata), StringComparer.OrdinalIgnoreCase)
        {
            ["creditPolicyOverage"] = "true"
        };
        return values;
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
