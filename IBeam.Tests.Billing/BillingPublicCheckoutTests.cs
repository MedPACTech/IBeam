using IBeam.Billing;
using IBeam.Billing.Api;
using IBeam.Billing.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingPublicCheckoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task StartCheckout_CreatesThreeTotalSeatPurchaseWithoutIdentity()
    {
        var fixture = CreateFixture();
        var result = await fixture.Service.StartCheckoutAsync(CreateRequest());
        var purchase = await fixture.Purchases.GetPurchaseAsync(result.PurchaseId);
        var attempts = await fixture.Attempts.ListAttemptsAsync(result.PurchaseId);
        var status = await fixture.Service.GetPurchaseStatusAsync(result.PurchaseId, result.StatusToken);

        Assert.AreEqual(1, result.LicenseQuantity);
        Assert.AreEqual(3, result.TotalSeats);
        Assert.AreEqual(75m, result.TotalAmount);
        Assert.IsNotNull(purchase);
        Assert.IsNull(purchase.TenantId);
        Assert.IsNull(purchase.UserId);
        Assert.AreEqual(BillingPurchaseStatuses.AwaitingPayment, purchase.Status);
        Assert.HasCount(1, attempts);
        Assert.AreEqual("cs_123", attempts[0].ProviderCheckoutSessionId);
        Assert.IsNotNull(status);
        Assert.AreEqual(BillingPurchaseNextActions.AwaitPayment, status.NextAction);
    }

    [TestMethod]
    public async Task StartCheckout_ReusesProviderSessionForIdempotencyKey()
    {
        var fixture = CreateFixture();
        var request = CreateRequest();
        var first = await fixture.Service.StartCheckoutAsync(request);
        var second = await fixture.Service.StartCheckoutAsync(request);

        Assert.AreEqual(first.PurchaseId, second.PurchaseId);
        Assert.AreEqual(1, fixture.Gateway.CreateCalls);
        Assert.AreEqual(1, fixture.Gateway.GetCalls);
    }

    [TestMethod]
    public async Task StartCheckout_RejectsUnlistedReturnOriginAndChangedReplay()
    {
        var fixture = CreateFixture();
        var unlisted = CreateRequest();
        unlisted.SuccessUrl = new Uri("https://evil.example.test/success");
        await Assert.ThrowsExactlyAsync<BillingException>(() => fixture.Service.StartCheckoutAsync(unlisted));

        var original = CreateRequest();
        await fixture.Service.StartCheckoutAsync(original);
        var changed = CreateRequest();
        changed.TotalSeats = 4;
        await Assert.ThrowsExactlyAsync<BillingException>(() => fixture.Service.StartCheckoutAsync(changed));
    }

    [TestMethod]
    public async Task PurchaseStatus_RejectsTamperedWrongAndExpiredTokens()
    {
        var fixture = CreateFixture();
        var checkout = await fixture.Service.StartCheckoutAsync(CreateRequest());

        Assert.IsNull(await fixture.Service.GetPurchaseStatusAsync(checkout.PurchaseId, checkout.StatusToken + "x"));
        Assert.IsNull(await fixture.Service.GetPurchaseStatusAsync(Guid.NewGuid(), checkout.StatusToken));
        fixture.Time.Advance(TimeSpan.FromMinutes(31));
        Assert.IsNull(await fixture.Service.GetPurchaseStatusAsync(checkout.PurchaseId, checkout.StatusToken));
    }

    [TestMethod]
    public void Controller_IsAnonymousAndRateLimited()
    {
        var type = typeof(CommerceCheckoutController);
        Assert.IsNotNull(type.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        var rateLimit = type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>()
            .Single();
        Assert.AreEqual(BillingApiServiceCollectionExtensions.PublicCheckoutRateLimitPolicy, rateLimit.PolicyName);
    }

    private static Fixture CreateFixture()
    {
        var offer = BillingOfferInfo.Create(
            "hubbsly-pro-monthly", "hubbsly", "hubbsly-pro", "Hubbsly Pro", null, "monthly", "USD",
            BillingOfferSeatPolicyInfo.Create(3, 3),
            BillingOfferPricingInfo.Create(perSeatAmount: 25m),
            [BillingPriceReferenceInfo.Create("stripe", "price_pro")]);
        var gateway = new FakeGateway();
        var time = new TestTimeProvider(Now);
        var purchases = new BillingPurchaseService(new InMemoryBillingPurchaseStore(), timeProvider: time);
        var attempts = new InMemoryBillingCheckoutAttemptStore();
        var service = new BillingPublicCheckoutService(
            new StaticOfferCatalog(offer), purchases, new BillingCheckoutGatewayResolver([gateway]),
            Options.Create(new BillingPublicCheckoutOptions
            {
                DefaultProviderName = "stripe",
                AllowedReturnOrigins = ["https://app.example.test"],
                StatusTokenSigningKey = "test-signing-key-at-least-thirty-two-characters",
                StatusTokenLifetimeMinutes = 30,
                PurchaseLifetimeMinutes = 60
            }), time, attempts);
        return new Fixture(service, purchases, attempts, gateway, time);
    }

    private static StartBillingCheckoutRequest CreateRequest()
        => new()
        {
            IdempotencyKey = "checkout-request-123",
            OfferKey = "hubbsly-pro-monthly",
            TotalSeats = 3,
            BuyerEmail = "buyer@example.test",
            SuccessUrl = new Uri("https://app.example.test/purchase/success"),
            CancelUrl = new Uri("https://app.example.test/plans")
        };

    private sealed record Fixture(BillingPublicCheckoutService Service, BillingPurchaseService Purchases, InMemoryBillingCheckoutAttemptStore Attempts, FakeGateway Gateway, TestTimeProvider Time);

    private sealed class StaticOfferCatalog(BillingOfferInfo offer) : IBillingOfferCatalogProvider
    {
        public Task<IReadOnlyList<BillingOfferInfo>> ListOffersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BillingOfferInfo>>([offer]);
        public Task<BillingOfferInfo?> GetOfferAsync(string offerKey, CancellationToken ct = default)
            => Task.FromResult<BillingOfferInfo?>(string.Equals(offer.Key, offerKey?.Trim(), StringComparison.OrdinalIgnoreCase) ? offer : null);
    }

    private sealed class FakeGateway : IBillingCheckoutGateway
    {
        public string ProviderName => "stripe";
        public int CreateCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task<BillingCheckoutSessionInfo> CreateCheckoutSessionAsync(CreateBillingCheckoutSessionRequest request, CancellationToken ct = default)
        {
            CreateCalls++;
            return Task.FromResult(BillingCheckoutSessionInfo.Create(request.CorrelationId, ProviderName, "cs_123", BillingCheckoutSessionStatuses.Open, new Uri("https://checkout.example.test/session")));
        }

        public Task<BillingCheckoutSessionInfo?> GetCheckoutSessionAsync(string checkoutSessionId, CancellationToken ct = default)
        {
            GetCalls++;
            return Task.FromResult<BillingCheckoutSessionInfo?>(BillingCheckoutSessionInfo.Create(Guid.NewGuid(), ProviderName, checkoutSessionId, BillingCheckoutSessionStatuses.Open, new Uri("https://checkout.example.test/session")));
        }

        public Task<BillingCustomerPortalSessionInfo> CreateCustomerPortalSessionAsync(CreateBillingCustomerPortalSessionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BillingVerifiedWebhookInfo> VerifyWebhookAsync(BillingWebhookRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
