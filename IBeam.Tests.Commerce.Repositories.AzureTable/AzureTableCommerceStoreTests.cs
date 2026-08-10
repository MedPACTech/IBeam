using IBeam.Billing;
using IBeam.Billing.Licensing;
using IBeam.Billing.Services;
using IBeam.Commerce.Repositories.AzureTable;
using IBeam.Credits;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Commerce.Repositories.AzureTable;

[TestClass]
public sealed class AzureTableCommerceStoreTests
{
    private static readonly Guid TenantId = Guid.Parse("6ab8c8ce-6d6f-4b2e-bca5-338fd243f40d");

    [TestMethod]
    public void Options_ValidateNormalizesDefaults()
    {
        var options = new AzureTableCommerceOptions
        {
            StorageConnectionString = "UseDevelopmentStorage=true",
            TablePrefix = "IBeamT"
        };

        options.Validate();

        Assert.AreEqual("IBeamTLicenses", options.FullTableName(options.LicensesTableName));
        Assert.IsTrue(options.CreateTablesIfNotExists);
    }

    [TestMethod]
    public void ServiceCollection_ReplacesCommerceStores()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IBeam:Commerce:AzureTable:StorageConnectionString"] = "UseDevelopmentStorage=true",
                ["IBeam:Commerce:AzureTable:CreateTablesIfNotExists"] = "false"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddIBeamCommerceAzureTableStores(configuration);
        using var provider = services.BuildServiceProvider();

        var licensing = provider.GetRequiredService<ILicensingStore>();
        var billing = provider.GetRequiredService<IBillingStore>();
        var purchases = provider.GetRequiredService<IBillingPurchaseStore>();
        var attempts = provider.GetRequiredService<IBillingCheckoutAttemptStore>();
        var claims = provider.GetRequiredService<IBillingPurchaseClaimStore>();
        var bindings = provider.GetRequiredService<IBillingSubscriptionProviderBindingStore>();
        var credits = provider.GetRequiredService<ICreditReservationStore>();

        Assert.IsInstanceOfType<AzureTableCommerceStore>(licensing);
        Assert.AreSame((object)licensing, billing);
        Assert.AreSame((object)licensing, purchases);
        Assert.AreSame((object)licensing, attempts);
        Assert.AreSame((object)licensing, claims);
        Assert.AreSame((object)licensing, bindings);
        Assert.AreSame((object)licensing, credits);
    }

    [TestMethod]
    public async Task LiveStore_PersistsLicensingBillingAndCredits()
    {
        var connectionString = Environment.GetEnvironmentVariable("IBEAM_AZURE_TABLE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("Set IBEAM_AZURE_TABLE_TEST_CONNECTION_STRING to run Azure Table persistence tests.");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IBeam:Commerce:AzureTable:StorageConnectionString"] = connectionString,
                ["IBeam:Commerce:AzureTable:TablePrefix"] = $"IB{Guid.NewGuid():N}"[..10],
                ["IBeam:Commerce:AzureTable:CreateTablesIfNotExists"] = "true"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddIBeamCommerceAzureTableStores(configuration);
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<AzureTableCommerceStore>();

        var license = new TenantLicenseRecord(
            Guid.NewGuid(),
            TenantId,
            "pro",
            "Pro",
            LicenseStatuses.Active,
            ["ai:chat"],
            new Dictionary<string, int>(),
            1,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>(),
            LicenseCommercialStatuses.Paid);
        await store.UpsertLicenseAsync(license);
        var assignment = new LicenseSeatAssignmentInfo(Guid.NewGuid(), TenantId, license.LicenseId, new LicenseSubject(LicenseSubjectTypes.User, "user-1"), DateTimeOffset.UtcNow, null, new Dictionary<string, string>());
        await store.AddAssignmentAsync(assignment);

        var customer = new BillingCustomerRecord(Guid.NewGuid(), TenantId, null, "Contoso", null, BillingModes.SelfServiceMonthly, BillingCustomerStatuses.Active, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string>());
        await store.SaveCustomerAsync(customer);
        var billingEvent = new BillingProviderEventRecord(Guid.NewGuid(), "stripe", "evt_live", "invoice.paid", BillingProviderEventStatuses.Received, DateTimeOffset.UtcNow, null, TenantId, null, null, null, null, null, null, new Dictionary<string, string>());
        await store.SaveProviderEventAsync(billingEvent);

        var purchaseService = new BillingPurchaseService(store);
        var purchase = await purchaseService.CreatePendingPurchaseAsync(new CreatePendingBillingPurchaseRequest
        {
            CorrelationId = Guid.NewGuid(),
            BuyerEmail = "buyer@example.test",
            OfferKey = "pro-monthly",
            ProductKey = "hubbsly",
            PlanKey = "pro",
            TotalSeats = 3,
            Currency = "USD",
            AmountSubtotal = 300m,
            AmountTotal = 300m,
            ProviderName = "stripe",
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
        });
        purchase = await purchaseService.ApplyProviderUpdateAsync(purchase.PurchaseId, new ApplyBillingPurchaseProviderUpdateRequest
        {
            ProviderName = "stripe",
            ProviderEventId = "evt_purchase_paid",
            EventType = BillingCommerceEventTypes.PaymentSucceeded,
            Status = BillingPurchaseStatuses.Paid,
            ProviderCheckoutSessionId = "cs_live",
            ProviderCustomerId = "cus_live",
            ProviderSubscriptionId = "sub_live"
        });
        var purchaseLicenseKey = Guid.NewGuid();
        purchase = await purchaseService.FulfillPaidPurchaseAsync(purchase.PurchaseId, purchaseLicenseKey);
        var attempt = BillingCheckoutAttemptInfo.Create(
            purchase.PurchaseId,
            "stripe",
            "cs_live",
            BillingCheckoutSessionStatuses.Completed,
            DateTimeOffset.UtcNow);
        await store.SaveAttemptAsync(attempt);

        var claim = new BillingPurchaseClaimRecord(
            Guid.NewGuid(), purchase.PurchaseId, purchaseLicenseKey,
            "HASHED_TOKEN_ONLY", "HASHED_EMAIL_ONLY",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30),
            null, null, null);
        await store.SaveIssuedAsync(claim);
        var claimed = await store.TryClaimAsync(claim.TokenHash, TenantId, Guid.NewGuid(), DateTimeOffset.UtcNow);

        var sourceBinding = new BillingSubscriptionProviderBindingInfo(
            Guid.NewGuid(), TenantId, Guid.NewGuid(), "stripe", "cus_live", "sub_live", "price_live",
            false, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow, new Dictionary<string, string>());
        var targetBinding = new BillingSubscriptionProviderBindingInfo(
            Guid.NewGuid(), TenantId, sourceBinding.BillingSubscriptionId, "paypal", "payer_live", "paypal_sub_live", "plan_live",
            true, DateTimeOffset.UtcNow, null, new Dictionary<string, string>());
        var migration = new BillingProviderMigrationRecord(
            Guid.NewGuid(), TenantId, sourceBinding.BillingSubscriptionId, "migration-live", "stripe", "paypal",
            purchaseLicenseKey, targetBinding.BindingId, DateTimeOffset.UtcNow);
        await store.CommitMigrationAsync(migration, sourceBinding, targetBinding);

        var expiredPurchase = await purchaseService.CreatePendingPurchaseAsync(new CreatePendingBillingPurchaseRequest
        {
            CorrelationId = Guid.NewGuid(),
            BuyerEmail = "expired@example.test",
            OfferKey = "individual",
            ProductKey = "hubbsly",
            PlanKey = "individual",
            TotalSeats = 1,
            Currency = "USD",
            AmountSubtotal = 10m,
            AmountTotal = 10m,
            ProviderName = "stripe",
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });
        var expiredRecord = await store.GetPurchaseAsync(expiredPurchase.PurchaseId);
        await store.SavePurchaseAsync(expiredRecord! with
        {
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = BillingPurchaseStatuses.Expired,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, expectedUpdatedUtc: expiredRecord.UpdatedUtc);

        var creditAccountId = Guid.NewGuid();
        var grant = CreditGrantInfo.Create(TenantId, creditAccountId, "ai-chat", 100, startsUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.AppendLedgerEntryAsync(grant.ToLedgerEntry());
        var reservation = new CreditReservationInfo(Guid.NewGuid(), TenantId, creditAccountId, "ai-chat", 10, 25, 25, null, CreditReservationStatuses.Active, "ai.chat", "req-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15), null, null, null, new Dictionary<string, string>());
        await store.SaveReservationAsync(reservation);

        Assert.IsNotNull(await store.GetLicenseAsync(TenantId, license.LicenseId));
        Assert.HasCount(1, await store.ListAssignmentsAsync(TenantId, license.LicenseId));
        Assert.IsNotNull(await store.GetCustomerAsync(TenantId, customer.BillingCustomerId));
        Assert.AreEqual(billingEvent.BillingProviderEventId, (await store.GetProviderEventByIdempotencyKeyAsync(billingEvent.IdempotencyKey))?.BillingProviderEventId);
        Assert.AreEqual(purchase.PurchaseId, (await store.GetPurchaseByCorrelationIdAsync(purchase.CorrelationId))?.PurchaseId);
        Assert.AreEqual(purchase.PurchaseId, (await store.GetPurchaseByProviderEventAsync("stripe", "evt_purchase_paid"))?.PurchaseId);
        Assert.AreEqual(purchase.PurchaseId, (await store.GetPurchaseByLicenseKeyAsync(purchaseLicenseKey))?.PurchaseId);
        Assert.AreEqual(attempt.CheckoutAttemptId, (await store.ListAttemptsAsync(purchase.PurchaseId)).Single().CheckoutAttemptId);
        Assert.AreEqual(claim.ClaimId, (await store.GetByPurchaseAsync(purchase.PurchaseId))?.ClaimId);
        Assert.AreEqual(claimed?.ClaimedUserId, (await store.GetByTokenHashAsync(claim.TokenHash))?.ClaimedUserId);
        Assert.AreEqual(migration.MigrationId, (await store.GetMigrationAsync(TenantId, migration.BillingSubscriptionId, "paypal", "migration-live"))?.MigrationId);
        Assert.AreEqual(1, (await store.ListBindingsAsync(TenantId, migration.BillingSubscriptionId)).Count(x => x.IsActive));
        Assert.AreEqual(1, await store.DeleteExpiredPurchasesAsync(DateTimeOffset.UtcNow));
        Assert.IsNull(await store.GetPurchaseAsync(expiredPurchase.PurchaseId));
        Assert.HasCount(1, await store.ListLedgerEntriesAsync(TenantId, creditAccountId, "ai-chat"));
        Assert.AreEqual(reservation.CreditReservationId, (await store.GetReservationByIdempotencyKeyAsync(TenantId, "req-1"))?.CreditReservationId);
    }
}
