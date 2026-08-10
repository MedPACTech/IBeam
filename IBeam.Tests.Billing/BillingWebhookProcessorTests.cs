using System.Reflection;
using System.Text;
using IBeam.Billing;
using IBeam.Billing.Api;
using IBeam.Billing.Services;
using Microsoft.AspNetCore.Authorization;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingWebhookProcessorTests
{
    [TestMethod]
    public async Task VerifiedPayment_UpdatesPurchaseAndReplayDoesNotDuplicateEvent()
    {
        var fixture = await Fixture.CreateAsync(BillingCommerceEventTypes.PaymentSucceeded);

        var first = await fixture.Processor.ProcessAsync("stripe", Webhook());
        var replay = await fixture.Processor.ProcessAsync("stripe", Webhook());
        var purchase = await fixture.Purchases.GetPurchaseAsync(fixture.PurchaseId);
        var events = await fixture.EventService.ListEventsAsync();

        Assert.AreEqual(BillingWebhookProcessingOutcomes.Processed, first.Outcome);
        Assert.IsFalse(first.IsReplay);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(BillingPurchaseStatuses.Paid, purchase?.Status);
        Assert.HasCount(1, events);
        Assert.AreEqual(BillingProviderEventStatuses.Processed, events[0].Status);
    }

    [TestMethod]
    public async Task InvalidSignature_DoesNotMutateBilling()
    {
        var fixture = await Fixture.CreateAsync(BillingCommerceEventTypes.PaymentSucceeded);
        fixture.Gateway.VerificationException = new BillingException("Invalid signature.");

        await Assert.ThrowsExactlyAsync<BillingException>(() =>
            fixture.Processor.ProcessAsync("stripe", Webhook()));

        var purchase = await fixture.Purchases.GetPurchaseAsync(fixture.PurchaseId);
        Assert.AreEqual(BillingPurchaseStatuses.AwaitingPayment, purchase?.Status);
        Assert.IsEmpty(await fixture.EventService.ListEventsAsync());
    }

    [TestMethod]
    public async Task OlderFailureAfterPayment_IsRecordedAsIgnored()
    {
        var fixture = await Fixture.CreateAsync(BillingCommerceEventTypes.PaymentSucceeded);
        fixture.Gateway.OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(2);
        await fixture.Processor.ProcessAsync("stripe", Webhook());

        fixture.Gateway.ProviderEventId = "evt_old_failure";
        fixture.Gateway.EventType = BillingCommerceEventTypes.PaymentFailed;
        fixture.Gateway.OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(1);
        var result = await fixture.Processor.ProcessAsync("stripe", Webhook());
        var purchase = await fixture.Purchases.GetPurchaseAsync(fixture.PurchaseId);

        Assert.AreEqual(BillingWebhookProcessingOutcomes.Ignored, result.Outcome);
        Assert.AreEqual("stale-event", result.Reason);
        Assert.AreEqual(BillingPurchaseStatuses.Paid, purchase?.Status);
    }

    [TestMethod]
    public async Task FailedProcessing_CanBeRetriedWithSameProviderEvent()
    {
        var fixture = await Fixture.CreateAsync(BillingCommerceEventTypes.PaymentSucceeded, failFirstUpdate: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Processor.ProcessAsync("stripe", Webhook()));
        var failedEvents = await fixture.EventService.ListEventsAsync();
        Assert.HasCount(1, failedEvents);
        Assert.AreEqual(BillingProviderEventStatuses.Failed, failedEvents[0].Status);

        var retried = await fixture.Processor.ProcessAsync("stripe", Webhook());
        var recoveredEvents = await fixture.EventService.ListEventsAsync();
        Assert.HasCount(1, recoveredEvents);

        Assert.AreEqual(BillingWebhookProcessingOutcomes.Processed, retried.Outcome);
        Assert.IsFalse(retried.IsReplay);
        Assert.AreEqual(BillingProviderEventStatuses.Processed, recoveredEvents[0].Status);
    }

    [TestMethod]
    public async Task UnsupportedEvent_IsIgnoredWithoutChangingPurchase()
    {
        var fixture = await Fixture.CreateAsync("provider.unknown");

        var result = await fixture.Processor.ProcessAsync("stripe", Webhook());
        var purchase = await fixture.Purchases.GetPurchaseAsync(fixture.PurchaseId);

        Assert.AreEqual(BillingWebhookProcessingOutcomes.Ignored, result.Outcome);
        Assert.AreEqual("unsupported-event", result.Reason);
        Assert.AreEqual(BillingPurchaseStatuses.AwaitingPayment, purchase?.Status);
    }

    [TestMethod]
    [DataRow(BillingCommerceEventTypes.CheckoutCompleted)]
    [DataRow(BillingCommerceEventTypes.PaymentSucceeded)]
    [DataRow(BillingCommerceEventTypes.PaymentFailed)]
    [DataRow(BillingCommerceEventTypes.SubscriptionRenewed)]
    [DataRow(BillingCommerceEventTypes.SubscriptionSeatsChanged)]
    [DataRow(BillingCommerceEventTypes.SubscriptionCanceled)]
    [DataRow(BillingCommerceEventTypes.PaymentRefunded)]
    [DataRow(BillingCommerceEventTypes.PaymentDisputed)]
    public void CommerceEventTypes_CoverProviderNeutralLifecycle(string eventType)
    {
        Assert.AreEqual(eventType, BillingCommerceEventTypes.Normalize(eventType));
    }

    [TestMethod]
    public void WebhookController_IsAnonymousAndProviderScoped()
    {
        var controllerType = typeof(BillingWebhooksController);
        var route = controllerType.GetCustomAttribute<Microsoft.AspNetCore.Mvc.RouteAttribute>();

        Assert.IsNotNull(controllerType.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.AreEqual("api/billing/webhooks/{providerName}", route?.Template);
    }

    private static BillingWebhookRequest Webhook()
        => BillingWebhookRequest.Create(
            Encoding.UTF8.GetBytes("{\"verified-by\":\"provider-adapter\"}"),
            new Dictionary<string, string> { ["provider-signature"] = "opaque" });

    private sealed class Fixture
    {
        private Fixture(
            FakeGateway gateway,
            IBillingPurchaseService purchases,
            BillingProviderEventService eventService,
            BillingWebhookProcessor processor,
            Guid purchaseId)
        {
            Gateway = gateway;
            Purchases = purchases;
            EventService = eventService;
            Processor = processor;
            PurchaseId = purchaseId;
        }

        public FakeGateway Gateway { get; }
        public IBillingPurchaseService Purchases { get; }
        public BillingProviderEventService EventService { get; }
        public BillingWebhookProcessor Processor { get; }
        public Guid PurchaseId { get; }

        public static async Task<Fixture> CreateAsync(string eventType, bool failFirstUpdate = false)
        {
            var realPurchases = new BillingPurchaseService(new InMemoryBillingPurchaseStore());
            var purchase = await realPurchases.CreatePendingPurchaseAsync(new CreatePendingBillingPurchaseRequest
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
            purchase = await realPurchases.ApplyProviderUpdateAsync(
                purchase.PurchaseId,
                new ApplyBillingPurchaseProviderUpdateRequest
                {
                    ProviderName = "stripe",
                    ProviderEventId = "checkout-created:cs_123",
                    EventType = "checkout.session.created",
                    Status = BillingPurchaseStatuses.AwaitingPayment,
                    ProviderCheckoutSessionId = "cs_123"
                });

            IBillingPurchaseService purchases = failFirstUpdate
                ? new FailOncePurchaseService(realPurchases)
                : realPurchases;
            var gateway = new FakeGateway(eventType, purchase.PurchaseId);
            var eventService = new BillingProviderEventService(new InMemoryBillingStore());
            var processor = new BillingWebhookProcessor(
                new BillingCheckoutGatewayResolver([gateway]),
                eventService,
                purchases);
            return new Fixture(gateway, purchases, eventService, processor, purchase.PurchaseId);
        }
    }

    private sealed class FakeGateway : IBillingCheckoutGateway
    {
        private readonly Guid _purchaseId;

        public FakeGateway(string eventType, Guid purchaseId)
        {
            EventType = eventType;
            _purchaseId = purchaseId;
        }

        public string ProviderName => "stripe";
        public string ProviderEventId { get; set; } = "evt_123";
        public string EventType { get; set; }
        public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow.AddMinutes(1);
        public Exception? VerificationException { get; set; }

        public Task<BillingVerifiedWebhookInfo> VerifyWebhookAsync(BillingWebhookRequest request, CancellationToken ct = default)
        {
            if (VerificationException is not null)
                throw VerificationException;
            return Task.FromResult(BillingVerifiedWebhookInfo.Create(
                ProviderName,
                ProviderEventId,
                EventType,
                OccurredUtc,
                providerCustomerId: "cus_123",
                providerSubscriptionId: "sub_123",
                providerCheckoutSessionId: "cs_123",
                metadata: new Dictionary<string, string> { ["purchaseId"] = _purchaseId.ToString("D") }));
        }

        public Task<BillingCheckoutSessionInfo> CreateCheckoutSessionAsync(CreateBillingCheckoutSessionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<BillingCheckoutSessionInfo?> GetCheckoutSessionAsync(string checkoutSessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<BillingCustomerPortalSessionInfo> CreateCustomerPortalSessionAsync(CreateBillingCustomerPortalSessionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FailOncePurchaseService : IBillingPurchaseService
    {
        private readonly IBillingPurchaseService _inner;
        private bool _fail = true;

        public FailOncePurchaseService(IBillingPurchaseService inner)
        {
            _inner = inner;
        }

        public Task<BillingPurchaseInfo?> GetPurchaseAsync(Guid purchaseId, CancellationToken ct = default)
            => _inner.GetPurchaseAsync(purchaseId, ct);

        public Task<BillingPurchaseInfo> CreatePendingPurchaseAsync(CreatePendingBillingPurchaseRequest request, CancellationToken ct = default)
            => _inner.CreatePendingPurchaseAsync(request, ct);

        public Task<BillingPurchaseInfo> ApplyProviderUpdateAsync(Guid purchaseId, ApplyBillingPurchaseProviderUpdateRequest request, CancellationToken ct = default)
        {
            if (_fail)
            {
                _fail = false;
                throw new InvalidOperationException("Transient storage failure.");
            }

            return _inner.ApplyProviderUpdateAsync(purchaseId, request, ct);
        }

        public Task RedactBuyerEmailAsync(Guid purchaseId, CancellationToken ct = default)
            => _inner.RedactBuyerEmailAsync(purchaseId, ct);
    }
}
