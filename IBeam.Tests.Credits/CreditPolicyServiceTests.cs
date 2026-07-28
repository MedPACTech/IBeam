using IBeam.Credits;
using IBeam.Credits.Services;

namespace IBeam.Tests.Credits;

[TestClass]
public sealed class CreditPolicyServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("c889f751-3d89-47c4-b036-adfe4fc2301e");

    [TestMethod]
    public async Task StrictPrepaid_DeniesBeforeOperationWhenMaxReservationIsNotAvailable()
    {
        var fixture = await CreateFixtureAsync(40);

        var decision = await fixture.Policy.BeginOperationAsync(TenantId, new BeginCreditOperationRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            PolicyMode = CreditPolicyModes.StrictPrepaid,
            EstimatedAmount = 20,
            MaxAmount = 50
        });

        Assert.IsFalse(decision.Approved);
        Assert.AreEqual(CreditPolicyDenialReasons.InsufficientCredits, decision.DenialReason);
    }

    [TestMethod]
    public async Task StrictPrepaid_ReservesMaxAndSettlesActual()
    {
        var fixture = await CreateFixtureAsync(100);
        var decision = await fixture.Policy.BeginOperationAsync(TenantId, new BeginCreditOperationRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            PolicyMode = CreditPolicyModes.StrictPrepaid,
            EstimatedAmount = 25,
            MaxAmount = 70
        });

        Assert.IsTrue(decision.Approved);
        Assert.IsNotNull(decision.Reservation);

        var result = await fixture.Policy.CompleteOperationAsync(TenantId, new CompleteCreditOperationRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            PolicyMode = CreditPolicyModes.StrictPrepaid,
            CreditReservationId = decision.Reservation.CreditReservationId,
            ActualAmount = 30
        });

        Assert.IsTrue(result.Approved);
        Assert.AreEqual(30, result.SettledAmount);
        Assert.AreEqual(0, result.OverageAmount);
        Assert.AreEqual(70, result.Balance?.Available);
    }

    [TestMethod]
    public async Task SoftOverage_ReservesEstimateAndRecordsOverageAfterActualUsageIsKnown()
    {
        var fixture = await CreateFixtureAsync(100);
        var decision = await fixture.Policy.BeginOperationAsync(TenantId, new BeginCreditOperationRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            PolicyMode = CreditPolicyModes.SoftOverage,
            EstimatedAmount = 30
        });

        var result = await fixture.Policy.CompleteOperationAsync(TenantId, new CompleteCreditOperationRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            PolicyMode = CreditPolicyModes.SoftOverage,
            CreditReservationId = decision.Reservation?.CreditReservationId,
            ActualAmount = 120,
            AllowOverage = true
        });

        Assert.IsTrue(result.Approved);
        Assert.AreEqual(30, result.SettledAmount);
        Assert.AreEqual(90, result.OverageAmount);
        Assert.AreEqual(120, result.Balance?.Debited);
        Assert.AreEqual(0, result.Balance?.Available);
    }

    [TestMethod]
    public async Task FailOpenMetering_RecordsActualUsageWithoutAReservation()
    {
        var fixture = await CreateFixtureAsync(0);

        var decision = await fixture.Policy.BeginOperationAsync(TenantId, new BeginCreditOperationRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            PolicyMode = CreditPolicyModes.FailOpenMetering,
            EstimatedAmount = 0
        });
        var result = await fixture.Policy.CompleteOperationAsync(TenantId, new CompleteCreditOperationRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            PolicyMode = CreditPolicyModes.FailOpenMetering,
            ActualAmount = 25
        });

        Assert.IsTrue(decision.Approved);
        Assert.IsNull(decision.Reservation);
        Assert.IsTrue(result.Approved);
        Assert.AreEqual(25, result.OverageAmount);
        Assert.AreEqual(25, result.Balance?.Debited);
    }

    [TestMethod]
    public async Task CapByRequest_RequiresAndEnforcesMaxCredits()
    {
        var fixture = await CreateFixtureAsync(100);

        var missingCap = await fixture.Policy.BeginOperationAsync(TenantId, new BeginCreditOperationRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            PolicyMode = CreditPolicyModes.CapByRequest,
            EstimatedAmount = 10
        });
        var exceededCap = await fixture.Policy.BeginOperationAsync(TenantId, new BeginCreditOperationRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            PolicyMode = CreditPolicyModes.CapByRequest,
            EstimatedAmount = 11,
            MaxCredits = 10
        });

        Assert.IsFalse(missingCap.Approved);
        Assert.AreEqual(CreditPolicyDenialReasons.MaxCreditsRequired, missingCap.DenialReason);
        Assert.IsFalse(exceededCap.Approved);
        Assert.AreEqual(CreditPolicyDenialReasons.MaxCreditsExceeded, exceededCap.DenialReason);
    }

    [TestMethod]
    public async Task Streaming_DeniesChunkThatWouldExceedRequestCap()
    {
        var fixture = await CreateFixtureAsync(100);

        var result = await fixture.Policy.RecordStreamingChunkAsync(TenantId, new RecordStreamingCreditChunkRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            ChunkAmount = 10,
            ConsumedToDate = 15,
            MaxCredits = 20
        });

        Assert.IsFalse(result.Approved);
        Assert.AreEqual(CreditPolicyDenialReasons.MaxCreditsExceeded, result.DenialReason);
    }

    [TestMethod]
    public async Task Streaming_RecordsChunkedUsageWhileCreditsRemain()
    {
        var fixture = await CreateFixtureAsync(100);

        var result = await fixture.Policy.RecordStreamingChunkAsync(TenantId, new RecordStreamingCreditChunkRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            ChunkAmount = 7,
            ConsumedToDate = 10,
            MaxCredits = 50
        });

        Assert.IsTrue(result.Approved);
        Assert.AreEqual(7, result.SettledAmount);
        Assert.AreEqual(93, result.Balance?.Available);
    }

    private static async Task<Fixture> CreateFixtureAsync(decimal grantAmount)
    {
        var store = new InMemoryCreditStore();
        var reservationService = new CreditReservationService(store);
        var policy = new CreditPolicyService(reservationService, reservationService, store);
        var accountId = Guid.NewGuid();
        if (grantAmount > 0)
        {
            var grant = CreditGrantInfo.Create(TenantId, accountId, "ai-chat", grantAmount, startsUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
            await store.AppendLedgerEntryAsync(grant.ToLedgerEntry());
        }

        return new Fixture(store, policy, accountId);
    }

    private sealed record Fixture(
        InMemoryCreditStore Store,
        CreditPolicyService Policy,
        Guid AccountId);
}
