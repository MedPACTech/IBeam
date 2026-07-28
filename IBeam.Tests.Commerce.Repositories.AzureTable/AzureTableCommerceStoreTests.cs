using IBeam.Billing;
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
        var credits = provider.GetRequiredService<ICreditReservationStore>();

        Assert.IsInstanceOfType<AzureTableCommerceStore>(licensing);
        Assert.AreSame((object)licensing, billing);
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

        var creditAccountId = Guid.NewGuid();
        var grant = CreditGrantInfo.Create(TenantId, creditAccountId, "ai-chat", 100, startsUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.AppendLedgerEntryAsync(grant.ToLedgerEntry());
        var reservation = new CreditReservationInfo(Guid.NewGuid(), TenantId, creditAccountId, "ai-chat", 10, 25, 25, null, CreditReservationStatuses.Active, "ai.chat", "req-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15), null, null, null, new Dictionary<string, string>());
        await store.SaveReservationAsync(reservation);

        Assert.IsNotNull(await store.GetLicenseAsync(TenantId, license.LicenseId));
        Assert.HasCount(1, await store.ListAssignmentsAsync(TenantId, license.LicenseId));
        Assert.IsNotNull(await store.GetCustomerAsync(TenantId, customer.BillingCustomerId));
        Assert.AreEqual(billingEvent.BillingProviderEventId, (await store.GetProviderEventByIdempotencyKeyAsync(billingEvent.IdempotencyKey))?.BillingProviderEventId);
        Assert.HasCount(1, await store.ListLedgerEntriesAsync(TenantId, creditAccountId, "ai-chat"));
        Assert.AreEqual(reservation.CreditReservationId, (await store.GetReservationByIdempotencyKeyAsync(TenantId, "req-1"))?.CreditReservationId);
    }
}
