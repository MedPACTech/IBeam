using IBeam.Ai.Completions;
using IBeam.Ai.Services.Completions;
using IBeam.Credits;
using IBeam.Licensing;
using IBeam.Licensing.Credits;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Ai;

[TestClass]
public sealed class CreditedAiCompletionServiceTests
{
    [TestMethod]
    public async Task CompleteAsync_UnmeteredProfile_BypassesTheGate()
    {
        var gate = new FakeGate(allowed: true);
        var sut = CreateSut(gate, metered: false);

        var result = await sut.CompleteAsync(new AiCompletionRequest { Messages = [AiMessage.User("hi")] });

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, gate.ExecuteCalls);
    }

    [TestMethod]
    public async Task CompleteAsync_MeteredProfile_ReservesEstimateAndSettlesActualTokenUsage()
    {
        var gate = new FakeGate(allowed: true);
        var sut = CreateSut(gate, metered: true);

        var result = await sut.CompleteAsync(MeteredRequest());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, gate.ExecuteCalls);
        Assert.AreEqual("ai-drafts", gate.LastRequest!.CreditBucketKey);
        Assert.AreEqual(2m, gate.LastRequest.EstimatedCredits);
        // Usage is 1M in / 500k out at 1 credit per million input and 4 per million output.
        Assert.AreEqual(3m, gate.LastActualCredits);
    }

    [TestMethod]
    public async Task CompleteAsync_MeteredProfileWithoutCreditContext_Throws()
    {
        var sut = CreateSut(new FakeGate(allowed: true), metered: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sut.CompleteAsync(new AiCompletionRequest { Messages = [AiMessage.User("hi")] }));
    }

    [TestMethod]
    public async Task CompleteAsync_WhenGateDenies_ReturnsDeniedWithoutCallingTheModel()
    {
        var gate = new FakeGate(allowed: false);
        var provider = new AiCompletionServiceTests.FakeProvider();
        var sut = CreateSut(gate, metered: true, provider);

        var result = await sut.CompleteAsync(MeteredRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AiCompletionStatus.Denied, result.Status);
        Assert.AreEqual(0, provider.Calls.Count);
    }

    [TestMethod]
    public void ComputeActualCredits_WithoutRates_SettlesFlatEstimateOnlyOnSuccess()
    {
        var profile = new AiProfileOptions { EstimatedCredits = 5m };

        Assert.AreEqual(5m, CreditedAiCompletionService.ComputeActualCredits(profile, new AiUsage(1, 1, 0, 0), succeeded: true));
        Assert.AreEqual(0m, CreditedAiCompletionService.ComputeActualCredits(profile, new AiUsage(1, 1, 0, 0), succeeded: false));
    }

    [TestMethod]
    public void ComputeActualCredits_WithRates_SettlesTokenUsageEvenOnFailure()
    {
        var profile = new AiProfileOptions
        {
            EstimatedCredits = 5m,
            CreditsPerMillionInputTokens = 2m,
            CreditsPerMillionOutputTokens = 10m
        };

        var credits = CreditedAiCompletionService.ComputeActualCredits(profile, new AiUsage(500_000, 100_000, 0, 0), succeeded: false);

        Assert.AreEqual(2m, credits);
    }

    private static AiCompletionRequest MeteredRequest() => new()
    {
        Messages = [AiMessage.User("hi")],
        Credit = new AiCreditContext
        {
            TenantId = Guid.NewGuid(),
            SubjectType = "user",
            SubjectId = "abram",
            Entitlement = "ai-drafting",
            CreditAccountId = Guid.NewGuid()
        }
    };

    private static CreditedAiCompletionService CreateSut(
        FakeGate gate,
        bool metered,
        AiCompletionServiceTests.FakeProvider? provider = null)
    {
        provider ??= new AiCompletionServiceTests.FakeProvider(
            AiCompletionResult.Ok("done", new AiUsage(1_000_000, 500_000, 0, 0)));

        var options = new AiOptions
        {
            DefaultProfile = "drafting",
            RetryBaseDelayMilliseconds = 0,
            Providers = { ["anthropic"] = new AiProviderOptions { Type = "anthropic", ApiKey = "key" } },
            Profiles =
            {
                ["drafting"] = new AiProfileOptions
                {
                    Provider = "anthropic",
                    Model = "claude-opus-5",
                    CreditBucketKey = metered ? "ai-drafts" : null,
                    EstimatedCredits = 2m,
                    CreditsPerMillionInputTokens = 1m,
                    CreditsPerMillionOutputTokens = 4m
                }
            }
        };

        var inner = new AiCompletionService(Options.Create(options), [provider], NullLogger<AiCompletionService>.Instance);
        return new CreditedAiCompletionService(inner, gate, new FakeCreditPolicy(), new FakeReservations());
    }

    private sealed class FakeGate : ILicenseCreditGate
    {
        private readonly bool _allowed;

        public FakeGate(bool allowed)
        {
            _allowed = allowed;
        }

        public int ExecuteCalls { get; private set; }
        public LicenseCreditGateRequest? LastRequest { get; private set; }
        public decimal? LastActualCredits { get; private set; }

        public Task<LicenseCreditGateResult> CheckAsync(LicenseCreditGateRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(BuildResult(request));
        }

        public async Task<LicenseCreditExecutionResult<T>> ExecuteAsync<T>(
            LicenseCreditGateRequest request,
            Func<CancellationToken, Task<CreditMeasuredOperationResult<T>>> operation,
            CancellationToken ct = default)
        {
            ExecuteCalls++;
            LastRequest = request;
            var gate = BuildResult(request);
            if (!_allowed)
                return new LicenseCreditExecutionResult<T>(false, false, default, gate, null);

            var measured = await operation(ct);
            LastActualCredits = measured.ActualCredits;
            return new LicenseCreditExecutionResult<T>(true, true, measured.Value, gate, null);
        }

        private LicenseCreditGateResult BuildResult(LicenseCreditGateRequest request)
        {
            var metadata = new Dictionary<string, string>();
            return _allowed
                ? LicenseCreditGateResult.Allow(
                    LicenseGateResult.Allow(request.TenantId, request.Subject, request.Entitlement, request.OperationName, Guid.NewGuid(), metadata),
                    null)
                : LicenseCreditGateResult.DenyLicense(
                    LicenseGateResult.Deny(request.TenantId, request.Subject, request.Entitlement, request.OperationName, LicenseGateDenialCodes.NoLicense, "denied", metadata));
        }
    }

    private sealed class FakeCreditPolicy : ICreditPolicyService
    {
        public Task<CreditOperationDecision> BeginOperationAsync(Guid tenantId, BeginCreditOperationRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CreditOperationSettlementResult> CompleteOperationAsync(Guid tenantId, CompleteCreditOperationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CreditOperationSettlementResult(
                true, request.PolicyMode, null, null, request.ActualAmount, request.ActualAmount, 0, null, null, null));

        public Task<CreditOperationSettlementResult> RecordStreamingChunkAsync(Guid tenantId, RecordStreamingCreditChunkRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeReservations : ICreditReservationService
    {
        public Task<CreditReservationInfo> ReserveAsync(Guid tenantId, ReserveCreditsRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CreditReservationInfo> SettleAsync(Guid tenantId, Guid creditReservationId, SettleCreditReservationRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CreditReservationInfo> ReleaseAsync(Guid tenantId, Guid creditReservationId, ReleaseCreditReservationRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CreditReservationInfo>> ExpireAsync(Guid tenantId, DateTimeOffset? asOfUtc = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
