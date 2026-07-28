using IBeam.Credits;
using IBeam.Credits.Services;

namespace IBeam.Tests.Credits;

[TestClass]
public sealed class CreditReservationServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("a76d3e8e-a91a-4fe0-88ec-85e8c14c0717");

    [TestMethod]
    public async Task ReserveAsync_ReservesMaxAmountWhenEnoughCreditsExist()
    {
        var fixture = await CreateFixtureAsync(100);

        var reservation = await fixture.Service.ReserveAsync(TenantId, new ReserveCreditsRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            EstimatedAmount = 20,
            MaxAmount = 50,
            IdempotencyKey = "request-1"
        });

        Assert.AreEqual(CreditReservationStatuses.Active, reservation.Status);
        Assert.AreEqual(20, reservation.EstimatedAmount);
        Assert.AreEqual(50, reservation.ReservedAmount);
    }

    [TestMethod]
    public async Task SettleAsync_RecordsActualDebitLessThanMax()
    {
        var fixture = await CreateFixtureAsync(100);
        var reservation = await ReserveAsync(fixture, maxAmount: 50);

        var settled = await fixture.Service.SettleAsync(TenantId, reservation.CreditReservationId, new SettleCreditReservationRequest
        {
            ActualAmount = 30,
            OperationName = "ai.chat.complete"
        });
        var balance = await BalanceAsync(fixture);

        Assert.AreEqual(CreditReservationStatuses.Settled, settled.Status);
        Assert.AreEqual(30, settled.ActualAmount);
        Assert.AreEqual(70, balance.Available);
    }

    [TestMethod]
    public async Task SettleAsync_RecordsActualDebitEqualToMax()
    {
        var fixture = await CreateFixtureAsync(100);
        var reservation = await ReserveAsync(fixture, maxAmount: 50);

        await fixture.Service.SettleAsync(TenantId, reservation.CreditReservationId, new SettleCreditReservationRequest
        {
            ActualAmount = 50
        });
        var balance = await BalanceAsync(fixture);

        Assert.AreEqual(50, balance.Available);
    }

    [TestMethod]
    public async Task SettleAsync_AllowsZeroActualUsageWithoutDebit()
    {
        var fixture = await CreateFixtureAsync(100);
        var reservation = await ReserveAsync(fixture, maxAmount: 50);

        var settled = await fixture.Service.SettleAsync(TenantId, reservation.CreditReservationId, new SettleCreditReservationRequest
        {
            ActualAmount = 0
        });
        var balance = await BalanceAsync(fixture);

        Assert.AreEqual(CreditReservationStatuses.Settled, settled.Status);
        Assert.AreEqual(0, settled.ActualAmount);
        Assert.AreEqual(100, balance.Available);
    }

    [TestMethod]
    public async Task ReleaseAsync_ReturnsReservedCreditsToAvailability()
    {
        var fixture = await CreateFixtureAsync(100);
        var reservation = await ReserveAsync(fixture, maxAmount: 80);

        var released = await fixture.Service.ReleaseAsync(TenantId, reservation.CreditReservationId, new ReleaseCreditReservationRequest
        {
            Reason = "operation-failed"
        });
        var balance = await BalanceAsync(fixture);

        Assert.AreEqual(CreditReservationStatuses.Released, released.Status);
        Assert.AreEqual("operation-failed", released.Metadata["releaseReason"]);
        Assert.AreEqual(100, balance.Available);
    }

    [TestMethod]
    public async Task ExpireAsync_ExpiresOldReservationsAndRestoresAvailability()
    {
        var fixture = await CreateFixtureAsync(100);
        var reservation = await fixture.Service.ReserveAsync(TenantId, new ReserveCreditsRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            EstimatedAmount = 10,
            MaxAmount = 40,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(1)
        });

        var expired = await fixture.Service.ExpireAsync(TenantId, DateTimeOffset.UtcNow.AddMinutes(1));
        var stored = await fixture.Store.GetReservationAsync(TenantId, reservation.CreditReservationId);
        var balance = await BalanceAsync(fixture);

        Assert.HasCount(1, expired);
        Assert.AreEqual(CreditReservationStatuses.Expired, stored?.Status);
        Assert.AreEqual(100, balance.Available);
    }

    [TestMethod]
    public async Task ReserveAsync_IsIdempotentByIdempotencyKey()
    {
        var fixture = await CreateFixtureAsync(100);
        var request = new ReserveCreditsRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            EstimatedAmount = 10,
            MaxAmount = 25,
            IdempotencyKey = "reserve-123"
        };

        var first = await fixture.Service.ReserveAsync(TenantId, request);
        var second = await fixture.Service.ReserveAsync(TenantId, request);

        Assert.AreEqual(first.CreditReservationId, second.CreditReservationId);
    }

    [TestMethod]
    public async Task RecordUsageAsync_WritesDebitWithoutReservation()
    {
        var fixture = await CreateFixtureAsync(100);

        var result = await fixture.Service.RecordUsageAsync(TenantId, new RecordCreditUsageRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            Amount = 15,
            OperationName = "trusted.enterprise"
        });

        Assert.AreEqual(CreditLedgerEntryTypes.Debit, result.LedgerEntry.EntryType);
        Assert.AreEqual(85, result.Balance.Available);
    }

    private static async Task<Fixture> CreateFixtureAsync(decimal grantAmount)
    {
        var store = new InMemoryCreditStore();
        var service = new CreditReservationService(store);
        var accountId = Guid.NewGuid();
        var grant = CreditGrantInfo.Create(TenantId, accountId, "ai-chat", grantAmount, startsUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.AppendLedgerEntryAsync(grant.ToLedgerEntry());
        return new Fixture(store, service, accountId);
    }

    private static Task<CreditReservationInfo> ReserveAsync(Fixture fixture, decimal maxAmount)
        => fixture.Service.ReserveAsync(TenantId, new ReserveCreditsRequest
        {
            CreditAccountId = fixture.AccountId,
            BucketKey = "ai-chat",
            EstimatedAmount = maxAmount / 2,
            MaxAmount = maxAmount
        });

    private static async Task<CreditBalanceInfo> BalanceAsync(Fixture fixture)
    {
        var entries = await fixture.Store.ListLedgerEntriesAsync(TenantId, fixture.AccountId, "ai-chat");
        return CreditLedgerCalculator.CalculateBalance(TenantId, fixture.AccountId, "ai-chat", entries);
    }

    private sealed record Fixture(
        InMemoryCreditStore Store,
        CreditReservationService Service,
        Guid AccountId);
}
