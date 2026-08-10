using IBeam.Billing;
using IBeam.Billing.Licensing;
using IBeam.Billing.Services;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingProviderMigrationTests
{
    private static readonly Guid TenantId = Guid.Parse("cc694e8a-768c-4e25-8d7f-b665a28e9931");

    [TestMethod]
    public async Task MigrateAsync_PreservesInternalSubscriptionLicenseAndSeats()
    {
        var fixture = await CreateFixtureAsync();
        var originalAssignment = await fixture.Assignments.AssignSeatAsync(
            TenantId,
            fixture.License.LicenseId,
            new AssignLicenseSeatRequest { Subject = new LicenseSubject(LicenseSubjectTypes.User, "owner") });

        var result = await fixture.Migrations.MigrateAsync(TenantId, PayPalRequest(fixture.Subscription.BillingSubscriptionId));
        var licenses = await fixture.Licenses.ListTenantLicensesAsync(TenantId);
        var assignments = await fixture.Assignments.ListAssignmentsAsync(TenantId, fixture.License.LicenseId);

        Assert.AreEqual(fixture.Subscription.BillingSubscriptionId, result.Subscription.BillingSubscriptionId);
        Assert.AreEqual(fixture.License.LicenseKey, result.LicenseKey);
        Assert.AreEqual("paypal", result.Subscription.ProviderName);
        Assert.AreEqual("paypal-sub-456", result.Subscription.ProviderSubscriptionId);
        Assert.AreEqual(4, result.Subscription.SeatQuantity);
        Assert.HasCount(1, licenses);
        Assert.AreEqual(fixture.License.LicenseId, licenses[0].LicenseId);
        Assert.AreEqual("paypal", licenses[0].ProviderName);
        Assert.AreEqual(4, licenses[0].SeatLimit);
        Assert.HasCount(1, assignments);
        Assert.AreEqual(originalAssignment.AssignmentId, assignments[0].AssignmentId);
        Assert.HasCount(2, result.ProviderBindings);
        Assert.AreEqual(1, result.ProviderBindings.Count(x => x.IsActive));
        Assert.AreEqual("paypal", result.ProviderBindings.Single(x => x.IsActive).ProviderName);
        Assert.IsFalse(result.WasReplay);
    }

    [TestMethod]
    public async Task MigrateAsync_ReplayDoesNotCreateAnotherLicenseBindingOrRenewal()
    {
        var fixture = await CreateFixtureAsync();
        var request = PayPalRequest(fixture.Subscription.BillingSubscriptionId);

        var first = await fixture.Migrations.MigrateAsync(TenantId, request);
        var replay = await fixture.Migrations.MigrateAsync(TenantId, request);

        Assert.AreEqual(first.MigrationId, replay.MigrationId);
        Assert.AreEqual(first.LicenseKey, replay.LicenseKey);
        Assert.IsTrue(replay.WasReplay);
        Assert.HasCount(2, replay.ProviderBindings);
        Assert.AreEqual(1, replay.ProviderBindings.Count(x => x.IsActive));
        Assert.HasCount(1, await fixture.Licenses.ListTenantLicensesAsync(TenantId));
    }

    [TestMethod]
    public async Task MigrateAsync_SecondMigrationRetiresCurrentBindingWithoutDuplicatingHistory()
    {
        var fixture = await CreateFixtureAsync();
        await fixture.Migrations.MigrateAsync(TenantId, PayPalRequest(fixture.Subscription.BillingSubscriptionId));

        var result = await fixture.Migrations.MigrateAsync(
            TenantId,
            new MigrateBillingProviderRequest
            {
                BillingSubscriptionId = fixture.Subscription.BillingSubscriptionId,
                TargetProviderName = "stripe",
                TargetProviderCustomerId = "stripe-customer-789",
                TargetProviderSubscriptionId = "stripe-sub-789",
                TargetProviderStatus = "active",
                TargetPrice = BillingPriceReferenceInfo.Create("stripe", "price_pro_v2", planKey: "hubbsly-pro"),
                SeatQuantity = 4,
                IdempotencyKey = "migration-002"
            });

        Assert.AreEqual(fixture.License.LicenseKey, result.LicenseKey);
        Assert.HasCount(3, result.ProviderBindings);
        Assert.AreEqual(1, result.ProviderBindings.Count(x => x.IsActive));
        Assert.AreEqual("stripe-sub-789", result.ProviderBindings.Single(x => x.IsActive).ProviderSubscriptionId);
        Assert.AreEqual(2, result.ProviderBindings.Count(x => !x.IsActive));
        Assert.HasCount(1, await fixture.Licenses.ListTenantLicensesAsync(TenantId));
    }

    [TestMethod]
    public async Task MigrateAsync_InvalidTargetLeavesSourceSubscriptionAndLicenseActive()
    {
        var fixture = await CreateFixtureAsync();
        var request = PayPalRequest(fixture.Subscription.BillingSubscriptionId);
        request.TargetSubscriptionStatus = BillingSubscriptionStatuses.Incomplete;

        await Assert.ThrowsExactlyAsync<BillingException>(() => fixture.Migrations.MigrateAsync(TenantId, request));

        var subscription = await fixture.Subscriptions.GetSubscriptionAsync(TenantId, fixture.Subscription.BillingSubscriptionId);
        var license = await fixture.Licenses.GetLicenseByKeyAsync(TenantId, fixture.License.LicenseKey);
        Assert.AreEqual("stripe", subscription?.ProviderName);
        Assert.AreEqual("stripe-sub-123", subscription?.ProviderSubscriptionId);
        Assert.AreEqual("stripe", license?.ProviderName);
        Assert.AreEqual(LicenseStatuses.Active, license?.Status);
        Assert.IsEmpty(await fixture.Bindings.ListBindingsAsync(TenantId, fixture.Subscription.BillingSubscriptionId));
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var billingStore = new InMemoryBillingStore();
        var subscriptions = new BillingSubscriptionService(billingStore);
        var bindingStore = new InMemoryBillingSubscriptionProviderBindingStore();
        var licensingStore = new InMemoryLicensingStore();
        var licenses = new TenantLicenseService(
            licensingStore,
            new ConfigurationLicensePlanCatalogProvider(Options.Create(new LicensingOptions())));
        var assignments = new LicenseSeatAssignmentService(licensingStore);
        var reconciler = new BillingLicenseReconciler(
            licenses,
            assignments,
            Options.Create(new BillingLicenseReconciliationOptions()));
        var migrations = new BillingProviderMigrationService(subscriptions, bindingStore, licenses, reconciler);
        var customerId = Guid.Parse("f6c06098-c6c6-429c-848c-ae949694fac5");
        var subscription = await subscriptions.CreateSubscriptionAsync(
            TenantId,
            new CreateBillingSubscriptionRequest
            {
                BillingCustomerId = customerId,
                ProductKey = "hubbsly",
                PlanKey = "hubbsly-pro",
                Status = BillingSubscriptionStatuses.Active,
                SeatQuantity = 3,
                ProviderName = "stripe",
                ProviderSubscriptionId = "stripe-sub-123",
                ProviderStatus = "active",
                Price = BillingPriceReferenceInfo.Create("stripe", "price_pro", planKey: "hubbsly-pro"),
                CurrentPeriodStartsUtc = DateTimeOffset.UtcNow.AddDays(-10),
                CurrentPeriodEndsUtc = DateTimeOffset.UtcNow.AddDays(20)
            });
        var reconciliation = await reconciler.ReconcileAsync(
            TenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = subscription,
                EventType = "invoice.paid",
                ProviderCustomerId = "stripe-customer-123"
            });

        return new Fixture(
            subscriptions,
            bindingStore,
            licenses,
            assignments,
            migrations,
            subscription,
            reconciliation.License!);
    }

    private static MigrateBillingProviderRequest PayPalRequest(Guid billingSubscriptionId)
        => new()
        {
            BillingSubscriptionId = billingSubscriptionId,
            TargetProviderName = "paypal",
            TargetProviderCustomerId = "paypal-customer-456",
            TargetProviderSubscriptionId = "paypal-sub-456",
            TargetProviderStatus = "active",
            TargetPrice = BillingPriceReferenceInfo.Create("paypal", "plan_pro", planKey: "hubbsly-pro"),
            SeatQuantity = 4,
            CurrentPeriodStartsUtc = DateTimeOffset.UtcNow,
            CurrentPeriodEndsUtc = DateTimeOffset.UtcNow.AddDays(30),
            IdempotencyKey = "migration-001"
        };

    private sealed record Fixture(
        BillingSubscriptionService Subscriptions,
        InMemoryBillingSubscriptionProviderBindingStore Bindings,
        TenantLicenseService Licenses,
        LicenseSeatAssignmentService Assignments,
        BillingProviderMigrationService Migrations,
        BillingSubscriptionInfo Subscription,
        TenantLicenseInfo License);
}
