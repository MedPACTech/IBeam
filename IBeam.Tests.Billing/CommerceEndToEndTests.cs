using System.Text;
using IBeam.Billing;
using IBeam.Billing.Licensing;
using IBeam.Billing.Services;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class CommerceEndToEndTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task HighestPlan_AnonymousCheckoutCreatesAndClaimsOneThreeSeatLicense()
    {
        var fixture = CreateFixture();
        var checkout = await fixture.Checkout.StartCheckoutAsync(Request("purchase-highest-plan"));
        var beforeIdentity = await fixture.Purchases.GetPurchaseAsync(checkout.PurchaseId);

        Assert.IsNull(beforeIdentity!.TenantId);
        Assert.IsNull(beforeIdentity.UserId);
        Assert.AreEqual(1, checkout.LicenseQuantity);
        Assert.AreEqual(3, checkout.TotalSeats);

        fixture.Gateway.Complete(checkout.PurchaseId, BillingCommerceEventTypes.PaymentSucceeded, "evt_paid_001");
        var payment = await fixture.Webhooks.ProcessAsync("stripe", Webhook());
        var replay = await fixture.Webhooks.ProcessAsync("stripe", Webhook());
        var fulfilled = await fixture.Purchases.GetPurchaseAsync(checkout.PurchaseId);

        Assert.AreEqual(BillingWebhookProcessingOutcomes.Processed, payment.Outcome);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(BillingPurchaseStatuses.Fulfilled, fulfilled!.Status);
        Assert.IsNotNull(fulfilled.LicenseKey);

        // Identity is created by the consuming app only after verified payment.
        var tenantId = Guid.NewGuid();
        var buyerUserId = Guid.NewGuid();
        var issued = await fixture.Claims.IssueAsync(checkout.PurchaseId);
        var claimed = await fixture.Claims.ClaimAsync(new()
        {
            ClaimToken = issued.ClaimToken,
            TenantId = tenantId,
            UserId = buyerUserId,
            VerifiedEmail = "buyer@example.test"
        });
        var completed = await fixture.Purchases.GetPurchaseAsync(checkout.PurchaseId);
        var publicStatus = await fixture.Checkout.GetPurchaseStatusAsync(checkout.PurchaseId, checkout.StatusToken);
        var licenses = await fixture.Licenses.ListTenantLicensesAsync(tenantId);

        Assert.AreEqual(BillingPurchaseStatuses.Claimed, completed!.Status);
        Assert.AreEqual(tenantId, completed.TenantId);
        Assert.AreEqual(buyerUserId, completed.UserId);
        Assert.AreEqual(BillingPurchaseNextActions.Complete, publicStatus!.NextAction);
        Assert.HasCount(1, licenses);
        Assert.AreEqual(fulfilled.LicenseKey, licenses[0].LicenseKey);
        Assert.AreEqual(3, licenses[0].SeatLimit);
        Assert.HasCount(1, claimed.Assignments);
        Assert.AreEqual(buyerUserId.ToString("D"), claimed.Assignments[0].Subject.SubjectId);
    }

    [TestMethod]
    [DataRow(BillingCommerceEventTypes.PaymentFailed, BillingPurchaseStatuses.Failed)]
    [DataRow(BillingCommerceEventTypes.SubscriptionCanceled, BillingPurchaseStatuses.Canceled)]
    public async Task UnsuccessfulCheckout_DoesNotCreateAClaimableLicense(string eventType, string expectedStatus)
    {
        var fixture = CreateFixture();
        var checkout = await fixture.Checkout.StartCheckoutAsync(Request($"purchase-{eventType}"));
        fixture.Gateway.Complete(checkout.PurchaseId, eventType, $"evt_{eventType}");

        await fixture.Webhooks.ProcessAsync("stripe", Webhook());
        var purchase = await fixture.Purchases.GetPurchaseAsync(checkout.PurchaseId);

        Assert.AreEqual(expectedStatus, purchase!.Status);
        Assert.IsNull(purchase.LicenseKey);
        await Assert.ThrowsExactlyAsync<BillingException>(() => fixture.Claims.IssueAsync(checkout.PurchaseId));
    }

    private static Fixture CreateFixture()
    {
        var time = new MutableTimeProvider(Now);
        var gateway = new ScenarioGateway(time);
        var resolver = new BillingCheckoutGatewayResolver([gateway]);
        var purchases = new BillingPurchaseService(new InMemoryBillingPurchaseStore(), timeProvider: time);
        var checkout = new BillingPublicCheckoutService(
            new StaticOfferCatalog(), purchases, resolver,
            Options.Create(new BillingPublicCheckoutOptions
            {
                DefaultProviderName = "stripe",
                AllowedReturnOrigins = ["https://app.example.test"],
                StatusTokenSigningKey = "commerce-e2e-signing-key-at-least-thirty-two-characters",
                StatusTokenLifetimeMinutes = 30,
                PurchaseLifetimeMinutes = 60
            }),
            time,
            new InMemoryBillingCheckoutAttemptStore());
        var fulfillment = new BillingPurchaseLicenseFulfillmentService(purchases);
        var events = new BillingProviderEventService(new InMemoryBillingStore(), timeProvider: time);
        var webhooks = new BillingWebhookProcessor(resolver, events, purchases, [fulfillment]);

        var licensingStore = new InMemoryLicensingStore();
        var licenses = new TenantLicenseService(licensingStore, new StaticPlanCatalog());
        var assignments = new LicenseSeatAssignmentService(licensingStore);
        var claims = new BillingPurchaseClaimService(
            purchases,
            new InMemoryBillingPurchaseClaimStore(),
            new LicenseSeatPolicyService(licenses, assignments),
            Options.Create(new BillingPurchaseClaimOptions()),
            time);
        return new Fixture(checkout, purchases, webhooks, claims, licenses, gateway);
    }

    private static StartBillingCheckoutRequest Request(string idempotencyKey)
        => new()
        {
            IdempotencyKey = idempotencyKey,
            OfferKey = "hubbsly-enterprise-monthly",
            TotalSeats = 3,
            BuyerEmail = "buyer@example.test",
            SuccessUrl = new Uri("https://app.example.test/purchase/success"),
            CancelUrl = new Uri("https://app.example.test/plans")
        };

    private static BillingWebhookRequest Webhook()
        => BillingWebhookRequest.Create(
            Encoding.UTF8.GetBytes("{\"signed\":true}"),
            new Dictionary<string, string> { ["provider-signature"] = "test-signature" });

    private sealed record Fixture(
        BillingPublicCheckoutService Checkout,
        BillingPurchaseService Purchases,
        BillingWebhookProcessor Webhooks,
        BillingPurchaseClaimService Claims,
        TenantLicenseService Licenses,
        ScenarioGateway Gateway);

    private sealed class StaticOfferCatalog : IBillingOfferCatalogProvider
    {
        private static readonly BillingOfferInfo Offer = BillingOfferInfo.Create(
            "hubbsly-enterprise-monthly",
            "hubbsly",
            "hubbsly-enterprise",
            "Hubbsly Enterprise",
            null,
            "monthly",
            "USD",
            BillingOfferSeatPolicyInfo.Create(3, 3),
            BillingOfferPricingInfo.Create(perSeatAmount: 50m),
            [BillingPriceReferenceInfo.Create("stripe", "price_enterprise")]);

        public Task<IReadOnlyList<BillingOfferInfo>> ListOffersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BillingOfferInfo>>([Offer]);

        public Task<BillingOfferInfo?> GetOfferAsync(string offerKey, CancellationToken ct = default)
            => Task.FromResult<BillingOfferInfo?>(string.Equals(Offer.Key, offerKey?.Trim(), StringComparison.OrdinalIgnoreCase) ? Offer : null);
    }

    private sealed class StaticPlanCatalog : ILicensePlanCatalogProvider
    {
        private static readonly LicensePlanInfo Plan = new(
            "hubbsly-enterprise",
            "Hubbsly Enterprise",
            null,
            ["tool:mcp"],
            new Dictionary<string, int>(),
            new Dictionary<string, string>());

        public Task<IReadOnlyList<LicensePlanInfo>> ListPlansAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LicensePlanInfo>>([Plan]);

        public Task<LicensePlanInfo?> GetPlanAsync(string planKey, CancellationToken ct = default)
            => Task.FromResult<LicensePlanInfo?>(string.Equals(Plan.Key, planKey?.Trim(), StringComparison.OrdinalIgnoreCase) ? Plan : null);
    }

    private sealed class ScenarioGateway(MutableTimeProvider time) : IBillingCheckoutGateway
    {
        private BillingVerifiedWebhookInfo? _verified;
        public string ProviderName => "stripe";

        public void Complete(Guid purchaseId, string eventType, string eventId)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            _verified = BillingVerifiedWebhookInfo.Create(
                ProviderName,
                eventId,
                eventType,
                time.GetUtcNow(),
                "cus_123",
                "sub_123",
                "cs_123",
                new Dictionary<string, string> { ["purchaseId"] = purchaseId.ToString("D") });
        }

        public Task<BillingCheckoutSessionInfo> CreateCheckoutSessionAsync(CreateBillingCheckoutSessionRequest request, CancellationToken ct = default)
            => Task.FromResult(BillingCheckoutSessionInfo.Create(
                request.CorrelationId,
                ProviderName,
                "cs_123",
                BillingCheckoutSessionStatuses.Open,
                new Uri("https://checkout.example.test/cs_123"),
                "cus_123",
                "sub_123"));

        public Task<BillingCheckoutSessionInfo?> GetCheckoutSessionAsync(string checkoutSessionId, CancellationToken ct = default)
            => Task.FromResult<BillingCheckoutSessionInfo?>(null);

        public Task<BillingCustomerPortalSessionInfo> CreateCustomerPortalSessionAsync(CreateBillingCustomerPortalSessionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<BillingVerifiedWebhookInfo> VerifyWebhookAsync(BillingWebhookRequest request, CancellationToken ct = default)
            => Task.FromResult(_verified ?? throw new InvalidOperationException("Complete the provider scenario before sending a webhook."));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
