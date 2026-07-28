using IBeam.Credits;
using IBeam.Licensing;
using IBeam.Services.Abstractions;

namespace IBeam.Licensing.Credits;

[IBeamOperation("licensing.credits.gate")]
public sealed class LicenseCreditGate : ILicenseCreditGate
{
    private readonly ILicenseGate _licenses;
    private readonly ICreditPolicyService _credits;
    private readonly ICreditReservationService _reservations;
    private readonly IServiceOperationExecutor _operations;

    public LicenseCreditGate(
        ILicenseGate licenses,
        ICreditPolicyService credits,
        ICreditReservationService reservations,
        IServiceOperationExecutor? operations = null)
    {
        _licenses = licenses;
        _credits = credits;
        _reservations = reservations;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("licensing.credits.gate.check")]
    public async Task<LicenseCreditGateResult> CheckAsync(
        LicenseCreditGateRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => CheckCoreAsync(request, token),
            new ServiceOperationExecutionOptions { TenantId = request?.TenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    [IBeamOperation("licensing.credits.gate.execute")]
    public async Task<LicenseCreditExecutionResult<T>> ExecuteAsync<T>(
        LicenseCreditGateRequest request,
        Func<CancellationToken, Task<CreditMeasuredOperationResult<T>>> operation,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ExecuteCoreAsync(request, operation, token),
            new ServiceOperationExecutionOptions { TenantId = request?.TenantId, EntityId = request?.CreditAccountId },
            ct).ConfigureAwait(false);

    private async Task<LicenseCreditGateResult> CheckCoreAsync(LicenseCreditGateRequest request, CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var license = await _licenses.CheckAsync(new LicenseGateRequest
        {
            TenantId = request.TenantId,
            Subject = request.Subject,
            Entitlement = request.Entitlement,
            OperationName = request.OperationName,
            Metadata = request.Metadata
        }, ct).ConfigureAwait(false);

        if (!license.Allowed)
            return LicenseCreditGateResult.DenyLicense(license);

        if (!request.RequiresCredits)
            return LicenseCreditGateResult.Allow(license, null);

        var credit = await _credits.BeginOperationAsync(request.TenantId, new BeginCreditOperationRequest
        {
            CreditAccountId = request.CreditAccountId,
            BucketKey = request.CreditBucketKey!,
            PolicyMode = request.CreditPolicyMode,
            EstimatedAmount = request.EstimatedCredits,
            MaxAmount = request.MaxCredits,
            MaxCredits = request.MaxCredits,
            ReservationExpiresUtc = request.ReservationExpiresUtc,
            OperationName = request.OperationName,
            IdempotencyKey = BuildIdempotencyKey(request),
            Metadata = BuildCreditMetadata(request)
        }, ct).ConfigureAwait(false);

        return credit.Approved
            ? LicenseCreditGateResult.Allow(license, credit)
            : LicenseCreditGateResult.DenyCredit(license, credit);
    }

    private async Task<LicenseCreditExecutionResult<T>> ExecuteCoreAsync<T>(
        LicenseCreditGateRequest request,
        Func<CancellationToken, Task<CreditMeasuredOperationResult<T>>> operation,
        CancellationToken ct)
    {
        if (operation is null)
            throw new ArgumentNullException(nameof(operation));

        var gate = await CheckCoreAsync(request, ct).ConfigureAwait(false);
        if (!gate.Allowed)
            return new LicenseCreditExecutionResult<T>(false, false, default, gate, null);

        try
        {
            var measured = await operation(ct).ConfigureAwait(false);
            if (!request.RequiresCredits)
                return new LicenseCreditExecutionResult<T>(true, true, measured.Value, gate, null);

            var settlement = await _credits.CompleteOperationAsync(request.TenantId, new CompleteCreditOperationRequest
            {
                CreditAccountId = request.CreditAccountId,
                BucketKey = request.CreditBucketKey!,
                PolicyMode = request.CreditPolicyMode,
                CreditReservationId = gate.Credit?.Reservation?.CreditReservationId,
                ActualAmount = measured.ActualCredits,
                MaxCredits = request.MaxCredits,
                AllowOverage = request.AllowOverage,
                OperationName = request.OperationName,
                IdempotencyKey = BuildIdempotencyKey(request, "settle"),
                Metadata = MergeMetadata(request.Metadata, measured.Metadata)
            }, ct).ConfigureAwait(false);

            return new LicenseCreditExecutionResult<T>(settlement.Approved, true, measured.Value, gate, settlement);
        }
        catch
        {
            await ReleaseReservationAsync(request, gate, ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReleaseReservationAsync(LicenseCreditGateRequest request, LicenseCreditGateResult gate, CancellationToken ct)
    {
        var reservation = gate.Credit?.Reservation;
        if (reservation is null || reservation.Status != CreditReservationStatuses.Active)
            return;

        await _reservations.ReleaseAsync(request.TenantId, reservation.CreditReservationId, new ReleaseCreditReservationRequest
        {
            Reason = "operation-failed",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["licenseCreditGate"] = "true",
                ["operationName"] = request.OperationName ?? string.Empty
            }
        }, ct).ConfigureAwait(false);
    }

    private static string? BuildIdempotencyKey(LicenseCreditGateRequest request, string? suffix = null)
    {
        if (!request.Metadata.TryGetValue("idempotencyKey", out var idempotencyKey) ||
            string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        return suffix is null ? idempotencyKey.Trim() : $"{idempotencyKey.Trim()}:{suffix}";
    }

    private static Dictionary<string, string> BuildCreditMetadata(LicenseCreditGateRequest request)
    {
        var metadata = MergeMetadata(request.Metadata, null);
        metadata["licenseEntitlement"] = request.Entitlement;
        metadata["licenseSubjectType"] = request.Subject.SubjectType;
        metadata["licenseSubjectId"] = request.Subject.SubjectId;
        return metadata;
    }

    private static Dictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string>? first,
        IReadOnlyDictionary<string, string>? second)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in first ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(item.Key))
                metadata[item.Key.Trim()] = item.Value?.Trim() ?? string.Empty;
        }

        foreach (var item in second ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(item.Key))
                metadata[item.Key.Trim()] = item.Value?.Trim() ?? string.Empty;
        }

        return metadata;
    }
}
