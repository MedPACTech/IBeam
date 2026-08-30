using System.Runtime.CompilerServices;
using IBeam.Ai.Completions;
using IBeam.Credits;
using IBeam.Licensing;
using IBeam.Licensing.Credits;

namespace IBeam.Ai.Services.Completions;

/// <summary>
/// Decorates the core completion service with entitlement checks and credit metering.
/// A metered profile (CreditBucketKey set) reserves EstimatedCredits before the call
/// and settles actual usage from the provider-reported token counts afterwards; the
/// reservation is released if the call throws. Unmetered profiles pass straight through.
/// </summary>
public sealed class CreditedAiCompletionService : IAiCompletionService
{
    private readonly AiCompletionService _inner;
    private readonly ILicenseCreditGate _gate;
    private readonly ICreditPolicyService _credits;
    private readonly ICreditReservationService _reservations;

    public CreditedAiCompletionService(
        AiCompletionService inner,
        ILicenseCreditGate gate,
        ICreditPolicyService credits,
        ICreditReservationService reservations)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _credits = credits ?? throw new ArgumentNullException(nameof(credits));
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
    }

    public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default)
    {
        var resolved = _inner.ResolveProfile(request);
        if (!resolved.Profile.IsMetered)
            return await _inner.CompleteAsync(request, ct).ConfigureAwait(false);

        var gateRequest = BuildGateRequest(resolved, request);
        var execution = await _gate.ExecuteAsync(
            gateRequest,
            async token =>
            {
                var result = await _inner.CompleteAsync(request, token).ConfigureAwait(false);
                return new CreditMeasuredOperationResult<AiCompletionResult>(
                    result,
                    ComputeActualCredits(resolved.Profile, result.Usage, result.Succeeded),
                    UsageMetadata(result.Usage));
            },
            ct).ConfigureAwait(false);

        if (!execution.Allowed && !execution.OperationExecuted)
            return AiCompletionResult.Fail(AiCompletionStatus.Denied, execution.Gate.Reason ?? "Credit or entitlement check denied the call.");

        return execution.Value!;
    }

    public async IAsyncEnumerable<AiCompletionChunk> StreamAsync(
        AiCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var resolved = _inner.ResolveProfile(request);
        if (!resolved.Profile.IsMetered)
        {
            await foreach (var chunk in _inner.StreamAsync(request, ct).ConfigureAwait(false))
                yield return chunk;
            yield break;
        }

        var gateRequest = BuildGateRequest(resolved, request);
        var gate = await _gate.CheckAsync(gateRequest, ct).ConfigureAwait(false);
        if (!gate.Allowed)
        {
            yield return AiCompletionChunk.Final(
                AiCompletionStatus.Denied, AiUsage.Empty, gate.Reason ?? "Credit or entitlement check denied the call.");
            yield break;
        }

        var usage = AiUsage.Empty;
        var settled = false;
        try
        {
            await foreach (var chunk in _inner.StreamAsync(request, ct).ConfigureAwait(false))
            {
                if (chunk.IsFinal)
                {
                    usage = chunk.FinalUsage ?? AiUsage.Empty;
                    var succeeded = chunk.FinalStatus == AiCompletionStatus.Ok;
                    await SettleAsync(resolved, gateRequest, gate, usage, succeeded, ct).ConfigureAwait(false);
                    settled = true;
                }

                yield return chunk;
            }

            if (!settled)
            {
                await SettleAsync(resolved, gateRequest, gate, usage, succeeded: false, ct).ConfigureAwait(false);
                settled = true;
            }
        }
        finally
        {
            if (!settled)
                await ReleaseReservationAsync(gateRequest, gate).ConfigureAwait(false);
        }
    }

    private LicenseCreditGateRequest BuildGateRequest(ResolvedAiProfile resolved, AiCompletionRequest request)
    {
        var credit = request.Credit ?? throw new InvalidOperationException(
            $"AI profile '{resolved.ProfileName}' is credit-metered (CreditBucketKey '{resolved.Profile.CreditBucketKey}') but the request carries no AiCreditContext.");

        return new LicenseCreditGateRequest
        {
            TenantId = credit.TenantId,
            Subject = new LicenseSubject(credit.SubjectType, credit.SubjectId),
            Entitlement = credit.Entitlement,
            OperationName = $"ai.completions.{resolved.ProfileName}",
            CreditAccountId = credit.CreditAccountId,
            CreditBucketKey = resolved.Profile.CreditBucketKey,
            EstimatedCredits = resolved.Profile.EstimatedCredits,
            CreditPolicyMode = resolved.Profile.CreditPolicyMode ?? CreditPolicyModes.StrictPrepaid,
            Metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task SettleAsync(
        ResolvedAiProfile resolved,
        LicenseCreditGateRequest gateRequest,
        LicenseCreditGateResult gate,
        AiUsage usage,
        bool succeeded,
        CancellationToken ct)
    {
        await _credits.CompleteOperationAsync(gateRequest.TenantId, new CompleteCreditOperationRequest
        {
            CreditAccountId = gateRequest.CreditAccountId,
            BucketKey = gateRequest.CreditBucketKey!,
            PolicyMode = gateRequest.CreditPolicyMode,
            CreditReservationId = gate.Credit?.Reservation?.CreditReservationId,
            ActualAmount = ComputeActualCredits(resolved.Profile, usage, succeeded),
            AllowOverage = gateRequest.AllowOverage,
            OperationName = gateRequest.OperationName,
            Metadata = UsageMetadata(usage)
        }, ct).ConfigureAwait(false);
    }

    private async Task ReleaseReservationAsync(LicenseCreditGateRequest gateRequest, LicenseCreditGateResult gate)
    {
        var reservation = gate.Credit?.Reservation;
        if (reservation is null || reservation.Status != CreditReservationStatuses.Active)
            return;

        await _reservations.ReleaseAsync(gateRequest.TenantId, reservation.CreditReservationId, new ReleaseCreditReservationRequest
        {
            Reason = "operation-failed",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["operationName"] = gateRequest.OperationName ?? string.Empty
            }
        }, CancellationToken.None).ConfigureAwait(false);
    }

    internal static decimal ComputeActualCredits(AiProfileOptions profile, AiUsage usage, bool succeeded)
    {
        if (profile.CreditsPerMillionInputTokens is null && profile.CreditsPerMillionOutputTokens is null)
            return succeeded ? profile.EstimatedCredits : 0m;

        // Token rates settle whatever was actually burned, succeeded or not.
        var input = (profile.CreditsPerMillionInputTokens ?? 0m) * usage.InputTokens / 1_000_000m;
        var output = (profile.CreditsPerMillionOutputTokens ?? 0m) * usage.OutputTokens / 1_000_000m;
        return input + output;
    }

    private static Dictionary<string, string> UsageMetadata(AiUsage usage) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["inputTokens"] = usage.InputTokens.ToString(),
        ["outputTokens"] = usage.OutputTokens.ToString(),
        ["cacheReadTokens"] = usage.CacheReadTokens.ToString(),
        ["cacheWriteTokens"] = usage.CacheWriteTokens.ToString()
    };
}
