using IBeam.Billing;
using IBeam.Billing.Licensing;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingLicenseReconciliationTests
{
    private static readonly Guid TenantId = Guid.Parse("1ff7c073-1e26-4e26-9811-14232679dd47");

    [TestMethod]
    public async Task ReconcileAsync_CreatesLicenseForInitialPurchase()
    {
        var fixture = CreateFixture();
        var subscription = Subscription("active", "price_pro");

        var result = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = subscription,
                EventType = "invoice.paid"
            });

        Assert.AreEqual(BillingLicenseReconciliationActions.Created, result.Action);
        Assert.IsNotNull(result.License);
        Assert.AreEqual("hubbsly-pro", result.License.PlanKey);
        Assert.AreEqual(5, result.License.SeatLimit);
        Assert.AreEqual("sub_123", result.License.ProviderSubscriptionId);
        Assert.AreEqual(LicenseCommercialStatuses.Paid, result.License.CommercialStatus);
    }

    [TestMethod]
    public async Task ReconcileAsync_RenewsExistingLicense()
    {
        var fixture = CreateFixture();
        var first = Subscription("active", "price_pro", periodEndsUtc: DateTimeOffset.UtcNow.AddDays(30));
        await fixture.Reconciler.ReconcileAsync(TenantId, new ReconcileBillingLicenseRequest { Subscription = first });
        var renewal = Subscription("active", "price_pro", periodEndsUtc: DateTimeOffset.UtcNow.AddDays(60));

        var result = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = renewal,
                EventType = "invoice.paid"
            });
        var licenses = await fixture.Licenses.ListTenantLicensesAsync(TenantId);

        Assert.AreEqual(BillingLicenseReconciliationActions.Renewed, result.Action);
        Assert.HasCount(1, licenses);
        Assert.IsTrue(result.License?.ExpiresUtc >= renewal.CurrentPeriodEndsUtc);
    }

    [TestMethod]
    public async Task ReconcileAsync_SuspendsOnCancellationByDefault()
    {
        var fixture = CreateFixture();
        await fixture.Reconciler.ReconcileAsync(TenantId, new ReconcileBillingLicenseRequest { Subscription = Subscription("active", "price_pro") });

        var result = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = Subscription(BillingSubscriptionStatuses.Canceled, "price_pro"),
                EventType = "customer.subscription.deleted"
            });

        Assert.AreEqual(BillingLicenseReconciliationActions.Suspended, result.Action);
        Assert.AreEqual(LicenseStatuses.Suspended, result.License?.Status);
        Assert.AreEqual(LicenseCommercialStatuses.Canceled, result.License?.CommercialStatus);
    }

    [TestMethod]
    public async Task ReconcileAsync_SuspendsOnPaymentFailure()
    {
        var fixture = CreateFixture();
        await fixture.Reconciler.ReconcileAsync(TenantId, new ReconcileBillingLicenseRequest { Subscription = Subscription("active", "price_pro") });

        var result = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = Subscription(BillingSubscriptionStatuses.PastDue, "price_pro"),
                EventType = "invoice.payment_failed"
            });

        Assert.AreEqual(BillingLicenseReconciliationActions.Suspended, result.Action);
        Assert.AreEqual(LicenseStatuses.Suspended, result.License?.Status);
        Assert.AreEqual(LicenseCommercialStatuses.PastDue, result.License?.CommercialStatus);
    }

    [TestMethod]
    public async Task ReconcileAsync_UsesSameFlowForManualGrant()
    {
        var fixture = CreateFixture();
        var subscription = Subscription(
            BillingSubscriptionStatuses.Active,
            priceId: null,
            planKey: "manual-plan",
            billingMode: BillingModes.ManualInvoice);

        var result = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest { Subscription = subscription });

        Assert.AreEqual(BillingLicenseReconciliationActions.Created, result.Action);
        Assert.AreEqual("manual-plan", result.License?.PlanKey);
        Assert.AreEqual(LicenseStatuses.Manual, result.License?.Status);
        Assert.AreEqual(LicenseCommercialStatuses.Manual, result.License?.CommercialStatus);
    }

    private static Fixture CreateFixture()
    {
        var licenses = new TenantLicenseService(
            new InMemoryLicensingStore(),
            new ConfigurationLicensePlanCatalogProvider(Options.Create(new LicensingOptions())));
        var options = Options.Create(new BillingLicenseReconciliationOptions
        {
            PriceMappings =
            [
                new BillingPricePlanMappingOptions
                {
                    ProviderName = "stripe",
                    PriceId = "price_pro",
                    PlanKey = "hubbsly-pro",
                    SeatLimit = 5,
                    Entitlements = ["app:use"]
                }
            ]
        });
        var reconciler = new BillingLicenseReconciler(licenses, options);
        return new Fixture(licenses, reconciler);
    }

    private static BillingSubscriptionInfo Subscription(
        string status,
        string? priceId,
        string? planKey = null,
        string billingMode = BillingModes.SelfServiceMonthly,
        DateTimeOffset? periodEndsUtc = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new BillingSubscriptionInfo(
            BillingSubscriptionId: Guid.Parse("3cc11767-09b2-4185-9945-b7a2bf367801"),
            TenantId: TenantId,
            UserId: Guid.Parse("f3e9fb36-fd41-4cef-9cec-f0362a332714"),
            BillingCustomerId: Guid.Parse("34a32a26-c955-41fb-998b-8ce3d7e7ec3d"),
            ProductKey: "hubbsly",
            PlanKey: planKey,
            BillingMode: billingMode,
            Status: status,
            SeatQuantity: 3,
            Price: priceId is null
                ? null
                : BillingPriceReferenceInfo.Create("stripe", priceId, productKey: "hubbsly", planKey: planKey),
            ProviderName: "stripe",
            ProviderSubscriptionId: "sub_123",
            ProviderStatus: status,
            CurrentPeriodStartsUtc: now.AddDays(-1),
            CurrentPeriodEndsUtc: periodEndsUtc ?? now.AddDays(30),
            CancelAtPeriodEnd: false,
            CreatedUtc: now,
            UpdatedUtc: now,
            Metadata: new Dictionary<string, string>());
    }

    private sealed record Fixture(
        TenantLicenseService Licenses,
        BillingLicenseReconciler Reconciler);
}
