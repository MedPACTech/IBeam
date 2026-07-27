using IBeam.Billing;
using System.Reflection;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingModelTests
{
    private static readonly Guid TenantId = Guid.Parse("b1cc6f06-7050-4729-a964-f74e02c28e01");
    private static readonly Guid UserId = Guid.Parse("26c82027-b9ab-4af7-9a2a-c73df56517d4");

    [TestMethod]
    public void BillingModes_NormalizeCommercialModels()
    {
        Assert.AreEqual(BillingModes.SelfServiceMonthly, BillingModes.Normalize(" Self_Service_Monthly "));
        Assert.AreEqual(BillingModes.AnnualContract, BillingModes.Normalize("ANNUAL-CONTRACT"));
        Assert.AreEqual(BillingModes.ManualInvoice, BillingModes.Normalize("manual_invoice"));
        Assert.AreEqual(BillingModes.Marketplace, BillingModes.Normalize("marketplace"));
        Assert.AreEqual(BillingModes.SupportManaged, BillingModes.Normalize("support-managed"));
        Assert.IsTrue(BillingModes.IsKnown("support_managed"));
    }

    [TestMethod]
    public void CustomerRecord_CarriesTenantAndOptionalUser()
    {
        var now = DateTimeOffset.UtcNow;
        var customer = new BillingCustomerRecord(
            Guid.NewGuid(),
            TenantId,
            UserId,
            "Hubbsly Care",
            "owner@example.com",
            " self_service_monthly ",
            " ACTIVE ",
            " stripe ",
            " cus_123 ",
            BillingPaymentMethodReferenceInfo.Create("stripe", "pm_123", type: "card", brand: "visa", lastFour: "4242"),
            now,
            now,
            new Dictionary<string, string> { [" buyer "] = " owner " }).ToInfo();

        Assert.AreEqual(TenantId, customer.TenantId);
        Assert.AreEqual(UserId, customer.UserId);
        Assert.AreEqual(BillingModes.SelfServiceMonthly, customer.BillingMode);
        Assert.AreEqual(BillingCustomerStatuses.Active, customer.Status);
        Assert.AreEqual("stripe", customer.ProviderName);
        Assert.AreEqual("owner", customer.Metadata["buyer"]);
        Assert.AreEqual("4242", customer.DefaultPaymentMethod?.LastFour);
    }

    [TestMethod]
    public void SubscriptionRecord_RepresentsEnterpriseSeatsAndPriceReference()
    {
        var now = DateTimeOffset.UtcNow;
        var subscription = new BillingSubscriptionRecord(
            Guid.NewGuid(),
            TenantId,
            null,
            Guid.NewGuid(),
            "hubbsly",
            "hubbsly-enterprise",
            BillingModes.AnnualContract,
            "active",
            25,
            BillingPriceReferenceInfo.Create(
                "stripe",
                "price_enterprise",
                productKey: "hubbsly",
                planKey: "hubbsly-enterprise",
                currency: "usd",
                unitAmount: 12000m,
                billingPeriod: "annual",
                billingMode: BillingModes.AnnualContract),
            "stripe",
            "sub_123",
            "active",
            now,
            now.AddYears(1),
            false,
            now,
            now,
            new Dictionary<string, string>()).ToInfo();

        Assert.IsNull(subscription.UserId);
        Assert.AreEqual(25, subscription.SeatQuantity);
        Assert.AreEqual(BillingModes.AnnualContract, subscription.BillingMode);
        Assert.AreEqual("USD", subscription.Price?.Currency);
        Assert.AreEqual("hubbsly-enterprise", subscription.Price?.PlanKey);
    }

    [TestMethod]
    public void InvoiceRecord_NormalizesManualInvoiceState()
    {
        var now = DateTimeOffset.UtcNow;
        var invoice = new BillingInvoiceRecord(
            Guid.NewGuid(),
            TenantId,
            null,
            Guid.NewGuid(),
            null,
            " manual_invoice ",
            " OPEN ",
            "quickbooks",
            "inv_123",
            "HB-100",
            "usd",
            499m,
            0m,
            now.AddDays(30),
            null,
            "https://example.test/invoices/HB-100",
            now,
            now,
            new Dictionary<string, string>()).ToInfo();

        Assert.AreEqual(BillingModes.ManualInvoice, invoice.BillingMode);
        Assert.AreEqual(BillingInvoiceStatuses.Open, invoice.Status);
        Assert.AreEqual("USD", invoice.Currency);
        Assert.AreEqual(499m, invoice.AmountDue);
    }

    [TestMethod]
    public void ProviderEvent_CreatesStableProviderScopedIdempotencyKey()
    {
        var now = DateTimeOffset.UtcNow;
        var providerEvent = new BillingProviderEventRecord(
            Guid.NewGuid(),
            " Stripe ",
            " evt_123 ",
            "customer.subscription.updated",
            " received ",
            now,
            null,
            TenantId,
            UserId,
            " cus_123 ",
            " sub_123 ",
            null,
            "application/json",
            "blob://billing-events/evt_123",
            new Dictionary<string, string> { [" source "] = " webhook " }).ToInfo();

        Assert.AreEqual("stripe:evt_123", providerEvent.IdempotencyKey);
        Assert.AreEqual(BillingProviderEventStatuses.Received, providerEvent.Status);
        Assert.AreEqual(TenantId, providerEvent.TenantId);
        Assert.AreEqual("webhook", providerEvent.Metadata["source"]);
    }

    [TestMethod]
    public void BillingCore_DoesNotReferenceLicensingAssembly()
    {
        var billingAssembly = typeof(BillingCustomerInfo).Assembly;
        var references = billingAssembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();

        CollectionAssert.DoesNotContain(references, "IBeam.Licensing");
    }
}
