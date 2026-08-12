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
        Assert.AreEqual(3, result.License.SeatLimit);
        Assert.AreEqual("sub_123", result.License.ProviderSubscriptionId);
        Assert.AreEqual(LicenseCommercialStatuses.Paid, result.License.CommercialStatus);
    }

    [TestMethod]
    public async Task ReconcileAsync_RenewsExistingLicense()
    {
        var fixture = CreateFixture();
        var first = Subscription("active", "price_pro", periodEndsUtc: DateTimeOffset.UtcNow.AddDays(30));
        var initial = await fixture.Reconciler.ReconcileAsync(TenantId, new ReconcileBillingLicenseRequest { Subscription = first });
        var assignment = await AssignSeatAsync(fixture, initial.License!, "renewal-user");
        var renewal = Subscription("active", "price_pro", periodEndsUtc: DateTimeOffset.UtcNow.AddDays(60), seatQuantity: 6);

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
        Assert.AreEqual(initial.License?.LicenseId, result.License?.LicenseId);
        Assert.AreEqual(6, result.License?.SeatLimit);
        Assert.AreEqual(assignment.AssignmentId, (await fixture.Assignments.ListAssignmentsAsync(TenantId, result.License!.LicenseId)).Single().AssignmentId);
        Assert.IsTrue(result.License?.ExpiresUtc >= renewal.CurrentPeriodEndsUtc);
    }

    [TestMethod]
    public async Task ReconcileAsync_ExpandsIndividualLicenseToMinimumMultiUserSeats()
    {
        var fixture = CreateDynamicFixture();
        var initial = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest { Subscription = Subscription("active", null, "flex", seatQuantity: 1) });

        var expanded = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest { Subscription = Subscription("active", null, "flex", seatQuantity: 2) });

        Assert.AreEqual(initial.License?.LicenseId, expanded.License?.LicenseId);
        Assert.AreEqual(3, expanded.License?.SeatLimit);
        Assert.AreEqual("multi-user", expanded.License?.Metadata["billingLicenseType"]);
        Assert.HasCount(1, await fixture.Licenses.ListTenantLicensesAsync(TenantId));
    }

    [TestMethod]
    public async Task ReconcileAsync_PreservesAssignedCapacityWhenSeatQuantityDrops()
    {
        var fixture = CreateDynamicFixture();
        var initial = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest { Subscription = Subscription("active", null, "flex", seatQuantity: 4) });
        for (var index = 0; index < 4; index++)
            await AssignSeatAsync(fixture, initial.License!, $"assigned-{index}");

        var reduced = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest { Subscription = Subscription("active", null, "flex", seatQuantity: 1) });

        Assert.AreEqual(initial.License?.LicenseId, reduced.License?.LicenseId);
        Assert.AreEqual(4, reduced.License?.SeatLimit);
        Assert.AreEqual("1", reduced.License?.Metadata["billingRequestedSeatLimit"]);
        Assert.AreEqual("adjusted-to-assignments", reduced.License?.Metadata["billingSeatState"]);
        Assert.HasCount(4, await fixture.Assignments.ListAssignmentsAsync(TenantId, reduced.License!.LicenseId));
    }

    [TestMethod]
    public async Task ReconcileAsync_CanExplicitlyAllowOverAssignedSeatReduction()
    {
        var fixture = CreateDynamicFixture();
        var initial = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest { Subscription = Subscription("active", null, "flex", seatQuantity: 4) });
        for (var index = 0; index < 4; index++)
            await AssignSeatAsync(fixture, initial.License!, $"over-assigned-{index}");

        var reduced = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = Subscription("active", null, "flex", seatQuantity: 1),
                SeatDecreaseBehavior = BillingLicenseSeatDecreaseBehaviors.AllowOverAssigned
            });

        Assert.AreEqual(3, reduced.License?.SeatLimit);
        Assert.AreEqual("over-assigned", reduced.License?.Metadata["billingSeatState"]);
        Assert.HasCount(4, await fixture.Assignments.ListAssignmentsAsync(TenantId, reduced.License!.LicenseId));
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
    public async Task ReconcileAsync_AppliesConfiguredGracePeriodOnPaymentFailure()
    {
        var fixture = CreateFixture();
        var effectiveUtc = DateTimeOffset.UtcNow.AddDays(30);
        var initial = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = Subscription("active", "price_pro", periodEndsUtc: effectiveUtc)
            });

        var result = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = Subscription(BillingSubscriptionStatuses.PastDue, "price_pro"),
                EventType = "invoice.payment_failed",
                PaymentFailureBehavior = BillingLicensePaymentFailureBehaviors.Grace,
                GracePeriodDays = 5,
                EffectiveUtc = effectiveUtc
            });

        Assert.AreEqual(initial.License?.LicenseId, result.License?.LicenseId);
        Assert.AreEqual(BillingLicenseReconciliationActions.Grace, result.Action);
        Assert.AreEqual(LicenseStatuses.Grace, result.License?.Status);
        Assert.AreEqual(LicenseCommercialStatuses.Grace, result.License?.CommercialStatus);
        Assert.AreEqual(effectiveUtc.AddDays(5), result.License?.GraceEndsUtc);
    }

    [TestMethod]
    public async Task ReconcileAsync_RevokesSameLicenseOnRefundByDefault()
    {
        var fixture = CreateFixture();
        var initial = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest { Subscription = Subscription("active", "price_pro") });

        var result = await fixture.Reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = Subscription("active", "price_pro"),
                EventType = "payment.refunded"
            });

        Assert.AreEqual(initial.License?.LicenseId, result.License?.LicenseId);
        Assert.AreEqual(BillingLicenseReconciliationActions.Revoked, result.Action);
        Assert.AreEqual(LicenseStatuses.Revoked, result.License?.Status);
        Assert.HasCount(1, await fixture.Licenses.ListTenantLicensesAsync(TenantId));
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
        => CreateFixture(new BillingLicenseReconciliationOptions
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

    private static Fixture CreateDynamicFixture()
        => CreateFixture(new BillingLicenseReconciliationOptions());

    private static Fixture CreateFixture(BillingLicenseReconciliationOptions reconciliationOptions)
    {
        var store = new InMemoryLicensingStore();
        var licenses = new TenantLicenseService(
            store,
            new ConfigurationLicensePlanCatalogProvider(Options.Create(new LicensingOptions())));
        var assignments = new LicenseSeatAssignmentService(store);
        var options = Options.Create(reconciliationOptions);
        var reconciler = new BillingLicenseReconciler(licenses, assignments, options);
        return new Fixture(licenses, assignments, reconciler);
    }

    private static BillingSubscriptionInfo Subscription(
        string status,
        string? priceId,
        string? planKey = null,
        string billingMode = BillingModes.SelfServiceMonthly,
        DateTimeOffset? periodEndsUtc = null,
        int seatQuantity = 3)
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
            SeatQuantity: seatQuantity,
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

    private static Task<LicenseSeatAssignmentInfo> AssignSeatAsync(Fixture fixture, TenantLicenseInfo license, string subjectId)
        => fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = new LicenseSubject(LicenseSubjectTypes.User, subjectId) });

    private sealed record Fixture(
        TenantLicenseService Licenses,
        LicenseSeatAssignmentService Assignments,
        BillingLicenseReconciler Reconciler);
}
