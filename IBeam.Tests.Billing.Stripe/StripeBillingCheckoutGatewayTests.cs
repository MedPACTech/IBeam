using System.Security.Cryptography;
using System.Text;
using IBeam.Billing;
using IBeam.Billing.Services;
using IBeam.Billing.Stripe;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace IBeam.Tests.Billing.Stripe;

[TestClass]
public sealed class StripeBillingCheckoutGatewayTests
{
    private const string WebhookSecret = "whsec_ibeam_test_secret";

    [TestMethod]
    public async Task Checkout_UsesTotalSeatQuantityAndIBeamMetadata()
    {
        var api = new FakeStripeBillingApiClient();
        var gateway = CreateGateway(api);
        var purchaseId = Guid.NewGuid();
        var request = CreateBillingCheckoutSessionRequest.Create(
            Guid.NewGuid(),
            "hubbsly-pro-monthly",
            3,
            new Uri("https://app.example.test/success"),
            new Uri("https://app.example.test/cancel"),
            "owner@example.test",
            "price_pro",
            new Dictionary<string, string> { ["purchaseId"] = purchaseId.ToString("D") });

        var result = await gateway.CreateCheckoutSessionAsync(request);

        Assert.AreEqual("stripe", result.ProviderName);
        Assert.AreEqual(3L, api.CheckoutOptions?.LineItems.Single().Quantity);
        Assert.AreEqual("price_pro", api.CheckoutOptions?.LineItems.Single().Price);
        Assert.AreEqual(purchaseId.ToString("D"), api.CheckoutOptions?.Metadata["purchaseId"]);
        Assert.AreEqual("hubbsly-pro-monthly", api.CheckoutOptions?.Metadata["offerKey"]);
        Assert.AreEqual("3", api.CheckoutOptions?.SubscriptionData.Metadata["totalSeats"]);
        Assert.AreEqual(request.CorrelationId.ToString("N"), api.RequestOptions?.IdempotencyKey);
    }

    [TestMethod]
    [DataRow("checkout.session.completed", BillingCommerceEventTypes.CheckoutCompleted)]
    [DataRow("payment_intent.succeeded", BillingCommerceEventTypes.PaymentSucceeded)]
    [DataRow("payment_intent.payment_failed", BillingCommerceEventTypes.PaymentFailed)]
    [DataRow("invoice.paid", BillingCommerceEventTypes.SubscriptionRenewed)]
    [DataRow("customer.subscription.updated", BillingCommerceEventTypes.SubscriptionSeatsChanged)]
    [DataRow("customer.subscription.deleted", BillingCommerceEventTypes.SubscriptionCanceled)]
    [DataRow("charge.refunded", BillingCommerceEventTypes.PaymentRefunded)]
    [DataRow("charge.dispute.created", BillingCommerceEventTypes.PaymentDisputed)]
    public async Task VerifiedWebhook_MapsStripeLifecycleEvents(string stripeType, string expectedType)
    {
        var gateway = CreateGateway(new FakeStripeBillingApiClient());
        var purchaseId = Guid.NewGuid();
        var json = EventJson(stripeType, purchaseId);

        var verified = await gateway.VerifyWebhookAsync(SignedWebhook(json));

        Assert.AreEqual(expectedType, verified.EventType);
        Assert.AreEqual("evt_123", verified.ProviderEventId);
        Assert.AreEqual(purchaseId.ToString("D"), verified.Metadata["purchaseId"]);
        if (stripeType == "customer.subscription.updated")
            Assert.AreEqual("3", verified.Metadata["totalSeats"]);
    }

    [TestMethod]
    public async Task InvalidWebhookSignature_IsRejected()
    {
        var gateway = CreateGateway(new FakeStripeBillingApiClient());
        var json = EventJson("checkout.session.completed", Guid.NewGuid());
        var request = BillingWebhookRequest.Create(
            Encoding.UTF8.GetBytes(json),
            new Dictionary<string, string> { ["Stripe-Signature"] = "t=1,v1=invalid" });

        await Assert.ThrowsExactlyAsync<BillingException>(() => gateway.VerifyWebhookAsync(request));
    }

    [TestMethod]
    public async Task CompletedCheckout_ReplayIsIdempotentThroughWebhookProcessor()
    {
        var gateway = CreateGateway(new FakeStripeBillingApiClient());
        var purchases = new BillingPurchaseService(new InMemoryBillingPurchaseStore());
        var purchase = await purchases.CreatePendingPurchaseAsync(new CreatePendingBillingPurchaseRequest
        {
            CorrelationId = Guid.NewGuid(),
            BuyerEmail = "buyer@example.test",
            OfferKey = "hubbsly-pro-monthly",
            ProductKey = "hubbsly",
            PlanKey = "hubbsly-pro",
            TotalSeats = 3,
            Currency = "USD",
            AmountSubtotal = 75m,
            AmountTotal = 75m,
            ProviderName = "stripe",
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        purchase = await purchases.ApplyProviderUpdateAsync(
            purchase.PurchaseId,
            new ApplyBillingPurchaseProviderUpdateRequest
            {
                ProviderName = "stripe",
                ProviderEventId = "checkout-created:cs_123",
                EventType = "checkout.session.created",
                Status = BillingPurchaseStatuses.AwaitingPayment
            });
        var events = new BillingProviderEventService(new InMemoryBillingStore());
        var processor = new BillingWebhookProcessor(new BillingCheckoutGatewayResolver([gateway]), events, purchases);
        var request = SignedWebhook(EventJson("checkout.session.completed", purchase.PurchaseId));

        var first = await processor.ProcessAsync("stripe", request);
        var replay = await processor.ProcessAsync("stripe", request);

        Assert.AreEqual(BillingWebhookProcessingOutcomes.Processed, first.Outcome);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(BillingPurchaseStatuses.Paid, (await purchases.GetPurchaseAsync(purchase.PurchaseId))?.Status);
        Assert.HasCount(1, await events.ListEventsAsync());
    }

    [TestMethod]
    public void Options_RequireSecretKeyAndWebhookSecret()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new StripeBillingOptions().Validate());
        Assert.ThrowsExactly<InvalidOperationException>(() => new StripeBillingOptions { SecretKey = "sk_test" }.Validate());
    }

    private static StripeBillingCheckoutGateway CreateGateway(IStripeBillingApiClient api)
        => new(
            api,
            Options.Create(new StripeBillingOptions
            {
                SecretKey = "sk_test_ibeam",
                WebhookSecret = WebhookSecret
            }));

    private static BillingWebhookRequest SignedWebhook(string json)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signed = $"{timestamp}.{json}";
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(WebhookSecret),
            Encoding.UTF8.GetBytes(signed)));
        return BillingWebhookRequest.Create(
            Encoding.UTF8.GetBytes(json),
            new Dictionary<string, string> { ["Stripe-Signature"] = $"t={timestamp},v1={signature}" });
    }

    private static string EventJson(string eventType, Guid purchaseId)
    {
        var objectId = eventType.StartsWith("customer.subscription.", StringComparison.Ordinal)
            ? "sub_123"
            : eventType == "checkout.session.completed" ? "cs_123" : "obj_123";
        return $$"""
        {
          "id": "evt_123",
          "object": "event",
          "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
          "type": "{{eventType}}",
          "data": {
            "object": {
              "id": "{{objectId}}",
              "object": "test_object",
              "customer": "cus_123",
              "subscription": "sub_123",
              "metadata": {
                "purchaseId": "{{purchaseId:D}}",
                "offerKey": "hubbsly-pro-monthly"
              },
              "items": { "data": [ { "quantity": 3 } ] }
            }
          }
        }
        """;
    }

    private sealed class FakeStripeBillingApiClient : IStripeBillingApiClient
    {
        public SessionCreateOptions? CheckoutOptions { get; private set; }
        public RequestOptions? RequestOptions { get; private set; }

        public Task<Session> CreateCheckoutSessionAsync(
            SessionCreateOptions options,
            RequestOptions requestOptions,
            CancellationToken ct)
        {
            CheckoutOptions = options;
            RequestOptions = requestOptions;
            return Task.FromResult(new Session
            {
                Id = "cs_123",
                Status = "open",
                Url = "https://checkout.stripe.test/cs_123",
                CustomerId = "cus_123",
                SubscriptionId = "sub_123",
                ClientReferenceId = options.ClientReferenceId,
                Metadata = options.Metadata
            });
        }

        public Task<Session> GetCheckoutSessionAsync(string sessionId, CancellationToken ct)
            => Task.FromResult(new Session
            {
                Id = sessionId,
                Status = "open",
                ClientReferenceId = Guid.NewGuid().ToString("D")
            });

        public Task<global::Stripe.BillingPortal.Session> CreatePortalSessionAsync(
            global::Stripe.BillingPortal.SessionCreateOptions options,
            RequestOptions requestOptions,
            CancellationToken ct)
            => Task.FromResult(new global::Stripe.BillingPortal.Session
            {
                Id = "bps_123",
                Url = "https://billing.stripe.test/session/bps_123"
            });
    }
}
