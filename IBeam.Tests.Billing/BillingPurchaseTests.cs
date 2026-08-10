using IBeam.Billing;
using IBeam.Billing.Services;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingPurchaseTests
{
    [TestMethod]
    public async Task CreatePendingPurchase_DoesNotRequireIdentityOrTenant()
    {
        var service = CreateService();
        var purchase = await service.CreatePendingPurchaseAsync(CreateRequest());

        Assert.AreNotEqual(Guid.Empty, purchase.PurchaseId);
        Assert.IsNull(purchase.TenantId);
        Assert.IsNull(purchase.UserId);
        Assert.IsNull(purchase.LicenseKey);
        Assert.AreEqual("buyer@example.test", purchase.BuyerEmail);
        Assert.AreEqual("hubbsly-pro-monthly", purchase.OfferKey);
        Assert.AreEqual(3, purchase.TotalSeats);
        Assert.AreEqual("USD", purchase.Currency);
        Assert.AreEqual(75m, purchase.AmountTotal);
        Assert.AreEqual(BillingPurchaseStatuses.Initiated, purchase.Status);
    }

    [TestMethod]
    public async Task CreatePendingPurchase_IsIdempotentByCorrelationId()
    {
        var service = CreateService();
        var request = CreateRequest();

        var first = await service.CreatePendingPurchaseAsync(request);
        var second = await service.CreatePendingPurchaseAsync(request);

        Assert.AreEqual(first.PurchaseId, second.PurchaseId);
        Assert.AreEqual(first.CorrelationId, second.CorrelationId);
    }

    [TestMethod]
    public async Task CreatePendingPurchase_IsIdempotentUnderConcurrency()
    {
        var service = CreateService();
        var correlationId = Guid.NewGuid();

        var purchases = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => service.CreatePendingPurchaseAsync(CreateRequest(correlationId))));

        Assert.HasCount(1, purchases.Select(x => x.PurchaseId).Distinct().ToList());
    }

    [TestMethod]
    public async Task ProviderUpdate_IsIdempotentAcrossPurchaseIds()
    {
        var service = CreateService();
        var first = await service.CreatePendingPurchaseAsync(CreateRequest());
        var second = await service.CreatePendingPurchaseAsync(CreateRequest(Guid.NewGuid()));
        var update = new ApplyBillingPurchaseProviderUpdateRequest
        {
            ProviderName = "stripe",
            ProviderEventId = "evt_paid_123",
            EventType = "checkout.session.completed",
            Status = BillingPurchaseStatuses.Paid,
            ProviderCheckoutSessionId = "cs_123",
            ProviderCustomerId = "cus_123",
            ProviderSubscriptionId = "sub_123"
        };

        var applied = await service.ApplyProviderUpdateAsync(first.PurchaseId, update);
        var replayedAgainstAnotherPurchase = await service.ApplyProviderUpdateAsync(second.PurchaseId, update);

        Assert.AreEqual(first.PurchaseId, applied.PurchaseId);
        Assert.AreEqual(first.PurchaseId, replayedAgainstAnotherPurchase.PurchaseId);
        Assert.AreEqual(BillingPurchaseStatuses.Paid, applied.Status);
        Assert.IsNotNull(applied.PaidUtc);
        Assert.AreEqual("evt_paid_123", applied.LastProviderEventId);
    }

    [TestMethod]
    public async Task ProviderUpdate_EnforcesLifecycleAndProviderBinding()
    {
        var service = CreateService();
        var purchase = await service.CreatePendingPurchaseAsync(CreateRequest());
        await service.ApplyProviderUpdateAsync(
            purchase.PurchaseId,
            ProviderUpdate("evt_paid", BillingPurchaseStatuses.Paid));

        await Assert.ThrowsExactlyAsync<BillingException>(() => service.ApplyProviderUpdateAsync(
            purchase.PurchaseId,
            ProviderUpdate("evt_failed", BillingPurchaseStatuses.Failed)));

        var mismatched = ProviderUpdate("evt_paypal", BillingPurchaseStatuses.Fulfilled);
        mismatched.ProviderName = "paypal";
        await Assert.ThrowsExactlyAsync<BillingException>(() => service.ApplyProviderUpdateAsync(purchase.PurchaseId, mismatched));
    }

    [TestMethod]
    public async Task RedactBuyerEmail_RemovesPiiAndTracksTimestamp()
    {
        var service = CreateService();
        var purchase = await service.CreatePendingPurchaseAsync(CreateRequest());

        await service.RedactBuyerEmailAsync(purchase.PurchaseId);
        var redacted = await service.GetPurchaseAsync(purchase.PurchaseId);

        Assert.IsNotNull(redacted);
        Assert.IsNull(redacted.BuyerEmail);
        Assert.IsNotNull(redacted.BuyerEmailRedactedUtc);
    }

    [TestMethod]
    public async Task CreatePendingPurchase_ValidatesSeatsAmountsAndExpiry()
    {
        var service = CreateService();
        var invalidSeats = CreateRequest();
        invalidSeats.TotalSeats = 0;
        await Assert.ThrowsExactlyAsync<BillingException>(() => service.CreatePendingPurchaseAsync(invalidSeats));

        var invalidAmount = CreateRequest(Guid.NewGuid());
        invalidAmount.AmountTotal = 74m;
        await Assert.ThrowsExactlyAsync<BillingException>(() => service.CreatePendingPurchaseAsync(invalidAmount));

        var expired = CreateRequest(Guid.NewGuid());
        expired.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await Assert.ThrowsExactlyAsync<BillingException>(() => service.CreatePendingPurchaseAsync(expired));
    }

    [TestMethod]
    public void PurchaseStatuses_CoverAnonymousPurchaseLifecycle()
    {
        var statuses = new[]
        {
            BillingPurchaseStatuses.Initiated,
            BillingPurchaseStatuses.AwaitingPayment,
            BillingPurchaseStatuses.Paid,
            BillingPurchaseStatuses.Fulfilled,
            BillingPurchaseStatuses.Claimed,
            BillingPurchaseStatuses.Expired,
            BillingPurchaseStatuses.Canceled,
            BillingPurchaseStatuses.Refunded,
            BillingPurchaseStatuses.Failed
        };

        foreach (var status in statuses)
            Assert.AreEqual(status, BillingPurchaseStatuses.Normalize(status));
    }

    private static BillingPurchaseService CreateService()
        => new(new InMemoryBillingPurchaseStore());

    private static CreatePendingBillingPurchaseRequest CreateRequest(Guid? correlationId = null)
        => new()
        {
            CorrelationId = correlationId ?? Guid.NewGuid(),
            BuyerEmail = " BUYER@EXAMPLE.TEST ",
            OfferKey = "hubbsly-pro-monthly",
            ProductKey = "hubbsly",
            PlanKey = "hubbsly-pro",
            TotalSeats = 3,
            Currency = "usd",
            AmountSubtotal = 75m,
            AmountTax = 0m,
            AmountTotal = 75m,
            ProviderName = "stripe",
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

    private static ApplyBillingPurchaseProviderUpdateRequest ProviderUpdate(string eventId, string status)
        => new()
        {
            ProviderName = "stripe",
            ProviderEventId = eventId,
            EventType = "test.event",
            Status = status
        };
}
