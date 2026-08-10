using System.Text;
using IBeam.Billing;
using IBeam.Billing.Services;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingCheckoutGatewayTests
{
    [TestMethod]
    public void CheckoutRequest_NormalizesIBeamOwnedFieldsAndTotalSeats()
    {
        var correlationId = Guid.NewGuid();
        var request = CreateBillingCheckoutSessionRequest.Create(
            correlationId,
            " hubbsly-pro-monthly ",
            3,
            new Uri("https://app.example.test/purchase/success"),
            new Uri("https://app.example.test/plans"),
            buyerEmail: " OWNER@EXAMPLE.TEST ",
            providerPriceId: " price_123 ",
            metadata: new Dictionary<string, string> { [" campaign "] = " public-site " });

        Assert.AreEqual(correlationId, request.CorrelationId);
        Assert.AreEqual("hubbsly-pro-monthly", request.OfferKey);
        Assert.AreEqual(3, request.TotalSeats);
        Assert.AreEqual("owner@example.test", request.BuyerEmail);
        Assert.AreEqual("price_123", request.ProviderPriceId);
        Assert.AreEqual("public-site", request.Metadata["campaign"]);
    }

    [TestMethod]
    public void CheckoutRequest_RejectsInvalidCorrelationSeatsAndUrls()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateBillingCheckoutSessionRequest.Create(
                Guid.Empty,
                "offer",
                3,
                new Uri("https://app.example.test/success"),
                new Uri("https://app.example.test/cancel")));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CreateBillingCheckoutSessionRequest.Create(
                Guid.NewGuid(),
                "offer",
                0,
                new Uri("https://app.example.test/success"),
                new Uri("https://app.example.test/cancel")));
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateBillingCheckoutSessionRequest.Create(
                Guid.NewGuid(),
                "offer",
                3,
                new Uri("/relative", UriKind.Relative),
                new Uri("https://app.example.test/cancel")));
    }

    [TestMethod]
    public async Task Resolver_SelectsReplaceableStripeAndPayPalGateways()
    {
        var stripe = new FakeCheckoutGateway("stripe");
        var paypal = new FakeCheckoutGateway("paypal");
        var resolver = new BillingCheckoutGatewayResolver([stripe, paypal]);
        var request = CreateBillingCheckoutSessionRequest.Create(
            Guid.NewGuid(),
            "hubbsly-pro-monthly",
            3,
            new Uri("https://app.example.test/success"),
            new Uri("https://app.example.test/cancel"));

        var stripeSession = await resolver.Resolve(" STRIPE ").CreateCheckoutSessionAsync(request);
        var paypalSession = await resolver.Resolve("paypal").CreateCheckoutSessionAsync(request);

        CollectionAssert.AreEqual(new[] { "paypal", "stripe" }, resolver.ProviderNames.ToArray());
        Assert.AreEqual("stripe", stripeSession.ProviderName);
        Assert.AreEqual("paypal", paypalSession.ProviderName);
        Assert.AreEqual(request.CorrelationId, stripe.LastCheckoutRequest?.CorrelationId);
        Assert.AreEqual(3, paypal.LastCheckoutRequest?.TotalSeats);
    }

    [TestMethod]
    public void Resolver_RejectsDuplicateAndMissingProviders()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new BillingCheckoutGatewayResolver(
                [new FakeCheckoutGateway("stripe"), new FakeCheckoutGateway("STRIPE")]));

        var resolver = new BillingCheckoutGatewayResolver([new FakeCheckoutGateway("stripe")]);
        Assert.ThrowsExactly<BillingException>(() => resolver.Resolve("paypal"));
    }

    [TestMethod]
    public async Task GatewayContract_CoversStatusPortalAndVerifiedWebhook()
    {
        var gateway = new FakeCheckoutGateway("stripe");
        var session = await gateway.GetCheckoutSessionAsync("cs_123");
        var portal = await gateway.CreateCustomerPortalSessionAsync(
            CreateBillingCustomerPortalSessionRequest.Create(
                Guid.NewGuid(),
                "cus_123",
                new Uri("https://app.example.test/billing")));
        var webhook = await gateway.VerifyWebhookAsync(
            BillingWebhookRequest.Create(
                Encoding.UTF8.GetBytes("{\"id\":\"evt_123\"}"),
                new Dictionary<string, string> { ["provider-signature"] = "opaque" }));

        Assert.IsNotNull(session);
        Assert.AreEqual(BillingCheckoutSessionStatuses.Open, session.Status);
        Assert.AreEqual("stripe", portal.ProviderName);
        Assert.AreEqual("evt_123", webhook.ProviderEventId);
        Assert.AreEqual("stripe", webhook.ProviderName);
    }

    [TestMethod]
    public void ProviderBinding_NormalizesOpaqueReferencesAndLifecycle()
    {
        var binding = BillingProviderBindingInfo.Create(
            Guid.NewGuid(),
            " Stripe ",
            " CHECKOUT_SESSION ",
            " cs_123 ");

        Assert.AreEqual("stripe", binding.ProviderName);
        Assert.AreEqual(BillingProviderBindingTypes.CheckoutSession, binding.BindingType);
        Assert.AreEqual("cs_123", binding.ProviderReferenceId);
        Assert.IsTrue(binding.IsActive);
        Assert.ThrowsExactly<ArgumentException>(() => BillingProviderBindingInfo.Create(
            Guid.NewGuid(),
            "stripe",
            "customer",
            "cus_123",
            isActive: true,
            retiredUtc: DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void BillingCore_RemainsFreeOfStripeAndPayPalSdkReferences()
    {
        var references = typeof(IBillingCheckoutGateway).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty)
            .ToArray();

        Assert.IsFalse(references.Any(x => x.Contains("Stripe", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(references.Any(x => x.Contains("PayPal", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class FakeCheckoutGateway : IBillingCheckoutGateway
    {
        public FakeCheckoutGateway(string providerName)
        {
            ProviderName = providerName;
        }

        public string ProviderName { get; }
        public CreateBillingCheckoutSessionRequest? LastCheckoutRequest { get; private set; }

        public Task<BillingCheckoutSessionInfo> CreateCheckoutSessionAsync(
            CreateBillingCheckoutSessionRequest request,
            CancellationToken ct = default)
        {
            LastCheckoutRequest = request;
            return Task.FromResult(BillingCheckoutSessionInfo.Create(
                request.CorrelationId,
                ProviderName,
                $"{ProviderName}_checkout",
                BillingCheckoutSessionStatuses.Open,
                new Uri($"https://checkout.example.test/{ProviderName}")));
        }

        public Task<BillingCheckoutSessionInfo?> GetCheckoutSessionAsync(
            string checkoutSessionId,
            CancellationToken ct = default)
            => Task.FromResult<BillingCheckoutSessionInfo?>(BillingCheckoutSessionInfo.Create(
                Guid.NewGuid(),
                ProviderName,
                checkoutSessionId,
                BillingCheckoutSessionStatuses.Open));

        public Task<BillingCustomerPortalSessionInfo> CreateCustomerPortalSessionAsync(
            CreateBillingCustomerPortalSessionRequest request,
            CancellationToken ct = default)
            => Task.FromResult(BillingCustomerPortalSessionInfo.Create(
                request.CorrelationId,
                ProviderName,
                $"{ProviderName}_portal",
                new Uri($"https://portal.example.test/{ProviderName}")));

        public Task<BillingVerifiedWebhookInfo> VerifyWebhookAsync(
            BillingWebhookRequest request,
            CancellationToken ct = default)
            => Task.FromResult(BillingVerifiedWebhookInfo.Create(
                ProviderName,
                "evt_123",
                "checkout.completed",
                request.ReceivedUtc,
                providerCheckoutSessionId: "cs_123"));
    }
}
