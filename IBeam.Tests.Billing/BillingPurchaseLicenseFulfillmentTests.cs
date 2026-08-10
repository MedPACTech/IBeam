using IBeam.Billing;
using IBeam.Billing.Licensing;
using IBeam.Billing.Services;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingPurchaseLicenseFulfillmentTests
{
    [TestMethod]
    public async Task IndividualPurchase_CreatesOneSeatLicenseGrant()
    {
        var fixture = new Fixture();
        var purchase = await fixture.CreatePaidPurchaseAsync(1);

        var license = await fixture.Fulfillment.FulfillAsync(new() { PurchaseId = purchase.PurchaseId });

        Assert.AreNotEqual(Guid.Empty, license.LicenseKey);
        Assert.AreEqual(1, license.TotalSeats);
        Assert.AreEqual("hubbsly-individual", license.PlanKey);
        Assert.AreEqual(BillingPurchaseStatuses.Fulfilled, license.Status);
    }

    [TestMethod]
    public async Task ThreeSeatPurchase_CreatesExactlyOneStableLicense()
    {
        var fixture = new Fixture();
        var purchase = await fixture.CreatePaidPurchaseAsync(3);

        var first = await fixture.Fulfillment.FulfillAsync(new() { PurchaseId = purchase.PurchaseId });
        var replay = await fixture.Fulfillment.FulfillAsync(new() { PurchaseId = purchase.PurchaseId });
        var stored = await fixture.Purchases.GetPurchaseAsync(purchase.PurchaseId);

        Assert.AreEqual(3, first.TotalSeats);
        Assert.AreEqual(first.LicenseKey, replay.LicenseKey);
        Assert.AreEqual(first.LicenseKey, stored?.LicenseKey);
        Assert.IsNotNull(stored?.FulfilledUtc);
    }

    [TestMethod]
    public async Task ExpansionPurchase_ReusesExistingLicenseKey()
    {
        var fixture = new Fixture();
        var individual = await fixture.CreatePaidPurchaseAsync(1);
        var original = await fixture.Fulfillment.FulfillAsync(new() { PurchaseId = individual.PurchaseId });
        var expansion = await fixture.CreatePaidPurchaseAsync(3);

        var expanded = await fixture.Fulfillment.FulfillAsync(new()
        {
            PurchaseId = expansion.PurchaseId,
            ExistingLicenseKey = original.LicenseKey
        });

        Assert.AreEqual(original.LicenseKey, expanded.LicenseKey);
        Assert.AreEqual(3, expanded.TotalSeats);
    }

    [TestMethod]
    public async Task UnpaidPurchase_CannotBeFulfilled()
    {
        var purchases = new BillingPurchaseService(new InMemoryBillingPurchaseStore());
        var fulfillment = new BillingPurchaseLicenseFulfillmentService(purchases);
        var pending = await purchases.CreatePendingPurchaseAsync(Request(3));

        await Assert.ThrowsExactlyAsync<BillingException>(() =>
            fulfillment.FulfillAsync(new() { PurchaseId = pending.PurchaseId }));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Purchases = new BillingPurchaseService(new InMemoryBillingPurchaseStore());
            Fulfillment = new BillingPurchaseLicenseFulfillmentService(Purchases);
        }

        public BillingPurchaseService Purchases { get; }
        public BillingPurchaseLicenseFulfillmentService Fulfillment { get; }

        public async Task<BillingPurchaseInfo> CreatePaidPurchaseAsync(int totalSeats)
        {
            var purchase = await Purchases.CreatePendingPurchaseAsync(Request(totalSeats));
            return await Purchases.ApplyProviderUpdateAsync(
                purchase.PurchaseId,
                new ApplyBillingPurchaseProviderUpdateRequest
                {
                    ProviderName = "stripe",
                    ProviderEventId = $"evt_paid_{purchase.PurchaseId:N}",
                    EventType = BillingCommerceEventTypes.PaymentSucceeded,
                    Status = BillingPurchaseStatuses.Paid,
                    ProviderCustomerId = "cus_replaceable",
                    ProviderSubscriptionId = "sub_replaceable"
                });
        }
    }

    private static CreatePendingBillingPurchaseRequest Request(int totalSeats)
        => new()
        {
            CorrelationId = Guid.NewGuid(),
            BuyerEmail = "buyer@example.test",
            OfferKey = totalSeats == 1 ? "hubbsly-individual-monthly" : "hubbsly-pro-monthly",
            ProductKey = "hubbsly",
            PlanKey = totalSeats == 1 ? "hubbsly-individual" : "hubbsly-pro",
            TotalSeats = totalSeats,
            Currency = "USD",
            AmountSubtotal = totalSeats * 25m,
            AmountTotal = totalSeats * 25m,
            ProviderName = "stripe",
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
}
