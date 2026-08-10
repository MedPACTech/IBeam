using IBeam.Billing;
using IBeam.Billing.Licensing;
using IBeam.Billing.Services;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingPurchaseClaimTests
{
    [TestMethod]
    public async Task NewBuyer_ClaimsThreeSeatLicenseAndReceivesFirstSeat()
    {
        var fixture = await Fixture.CreateAsync(3);
        var issued = await fixture.Claims.IssueAsync(fixture.PurchaseId);
        var stored = await fixture.Store.GetByPurchaseAsync(fixture.PurchaseId);

        var claimed = await fixture.Claims.ClaimAsync(fixture.Request(issued.ClaimToken));

        Assert.IsNotNull(stored);
        Assert.AreNotEqual(issued.ClaimToken, stored.TokenHash);
        Assert.AreEqual(fixture.LicenseKey, claimed.License.LicenseKey);
        Assert.AreEqual(3, claimed.License.SeatLimit);
        Assert.HasCount(1, claimed.Assignments);
        Assert.AreEqual(fixture.UserId.ToString("D"), claimed.Assignments[0].Subject.SubjectId);
        Assert.AreEqual(fixture.PurchaseId.ToString("D"), claimed.License.Metadata["billingPurchaseId"]);
    }

    [TestMethod]
    public async Task ExistingBuyer_RetryReturnsSameLicenseAndSeat()
    {
        var fixture = await Fixture.CreateAsync(1);
        var issued = await fixture.Claims.IssueAsync(fixture.PurchaseId);
        var request = fixture.Request(issued.ClaimToken);

        var first = await fixture.Claims.ClaimAsync(request);
        var retry = await fixture.Claims.ClaimAsync(request);

        Assert.IsFalse(first.IsRetry);
        Assert.IsTrue(retry.IsRetry);
        Assert.AreEqual(first.License.LicenseKey, retry.License.LicenseKey);
        Assert.AreEqual(first.Assignments[0].AssignmentId, retry.Assignments[0].AssignmentId);
    }

    [TestMethod]
    public async Task WrongBuyer_DoesNotConsumeClaim()
    {
        var fixture = await Fixture.CreateAsync(3);
        var issued = await fixture.Claims.IssueAsync(fixture.PurchaseId);
        var wrong = fixture.Request(issued.ClaimToken);
        wrong.VerifiedEmail = "attacker@example.test";

        await Assert.ThrowsExactlyAsync<BillingException>(() => fixture.Claims.ClaimAsync(wrong));
        var claimed = await fixture.Claims.ClaimAsync(fixture.Request(issued.ClaimToken));

        Assert.AreEqual(fixture.UserId, claimed.UserId);
    }

    [TestMethod]
    public async Task ExpiredClaim_IsRejected()
    {
        var fixture = await Fixture.CreateAsync(3);
        var issued = await fixture.Claims.IssueAsync(fixture.PurchaseId);
        fixture.Time.Advance(TimeSpan.FromMinutes(31));

        await Assert.ThrowsExactlyAsync<BillingException>(() =>
            fixture.Claims.ClaimAsync(fixture.Request(issued.ClaimToken)));
    }

    [TestMethod]
    public async Task ClaimedToken_RejectsCrossTenantAndDifferentUserReuse()
    {
        var fixture = await Fixture.CreateAsync(3);
        var issued = await fixture.Claims.IssueAsync(fixture.PurchaseId);
        await fixture.Claims.ClaimAsync(fixture.Request(issued.ClaimToken));

        var crossTenant = fixture.Request(issued.ClaimToken);
        crossTenant.TenantId = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<BillingException>(() => fixture.Claims.ClaimAsync(crossTenant));

        var otherUser = fixture.Request(issued.ClaimToken);
        otherUser.UserId = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<BillingException>(() => fixture.Claims.ClaimAsync(otherUser));
    }

    private sealed class Fixture
    {
        private Fixture(
            BillingPurchaseClaimService claims,
            InMemoryBillingPurchaseClaimStore store,
            MutableTimeProvider time,
            Guid purchaseId,
            Guid licenseKey)
        {
            Claims = claims;
            Store = store;
            Time = time;
            PurchaseId = purchaseId;
            LicenseKey = licenseKey;
        }

        public BillingPurchaseClaimService Claims { get; }
        public InMemoryBillingPurchaseClaimStore Store { get; }
        public MutableTimeProvider Time { get; }
        public Guid PurchaseId { get; }
        public Guid LicenseKey { get; }
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();

        public ClaimBillingPurchaseLicenseRequest Request(string token)
            => new()
            {
                ClaimToken = token,
                TenantId = TenantId,
                UserId = UserId,
                VerifiedEmail = "buyer@example.test"
            };

        public static async Task<Fixture> CreateAsync(int seats)
        {
            var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
            var purchases = new BillingPurchaseService(new InMemoryBillingPurchaseStore(), timeProvider: time);
            var pending = await purchases.CreatePendingPurchaseAsync(new CreatePendingBillingPurchaseRequest
            {
                CorrelationId = Guid.NewGuid(),
                BuyerEmail = "buyer@example.test",
                OfferKey = "hubbsly-pro-monthly",
                ProductKey = "hubbsly",
                PlanKey = "hubbsly-pro",
                TotalSeats = seats,
                Currency = "USD",
                AmountSubtotal = seats * 25m,
                AmountTotal = seats * 25m,
                ProviderName = "stripe",
                ExpiresUtc = time.GetUtcNow().AddHours(1)
            });
            var paid = await purchases.ApplyProviderUpdateAsync(
                pending.PurchaseId,
                new ApplyBillingPurchaseProviderUpdateRequest
                {
                    ProviderName = "stripe",
                    ProviderEventId = "evt_paid",
                    EventType = BillingCommerceEventTypes.PaymentSucceeded,
                    Status = BillingPurchaseStatuses.Paid,
                    ProviderCustomerId = "cus_123",
                    ProviderSubscriptionId = "sub_123"
                });
            var fulfillment = new BillingPurchaseLicenseFulfillmentService(purchases);
            var grant = await fulfillment.FulfillAsync(new() { PurchaseId = paid.PurchaseId });

            var licensingStore = new InMemoryLicensingStore();
            var licenseService = new TenantLicenseService(licensingStore, new FakePlanCatalog());
            var seatsService = new LicenseSeatAssignmentService(licensingStore);
            var policies = new LicenseSeatPolicyService(licenseService, seatsService);
            var claimStore = new InMemoryBillingPurchaseClaimStore();
            var claims = new BillingPurchaseClaimService(
                purchases,
                claimStore,
                policies,
                Options.Create(new BillingPurchaseClaimOptions { TokenLifetimeMinutes = 30 }),
                time);
            return new Fixture(claims, claimStore, time, paid.PurchaseId, grant.LicenseKey);
        }
    }

    private sealed class FakePlanCatalog : ILicensePlanCatalogProvider
    {
        public Task<IReadOnlyList<LicensePlanInfo>> ListPlansAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LicensePlanInfo>>(
                [new LicensePlanInfo("hubbsly-pro", "Hubbsly Pro", null, ["tool:mcp"], new Dictionary<string, int>(), new Dictionary<string, string>())]);

        public async Task<LicensePlanInfo?> GetPlanAsync(string planKey, CancellationToken ct = default)
            => (await ListPlansAsync(ct)).FirstOrDefault(x => string.Equals(x.Key, planKey, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public MutableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
