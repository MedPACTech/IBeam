using System.Text;
using IBeam.AccessControl;
using IBeam.Billing;
using IBeam.Billing.Services;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Billing;

/// <summary>
/// Drives a signed provider webhook through the container a real host builds: billing and
/// licensing registered the normal way, <c>AddIBeamLicensedServiceOperations()</c> swapping in the
/// licensed executor, and an authorizer registered so service-operation authorization is live.
/// <para>
/// This is the composition the webhook system scope first shipped without being tested in. The
/// processor tests construct services by hand with the plain executor, which honours the scope;
/// a host that calls <c>AddIBeamLicensedServiceOperations()</c> gets
/// <see cref="LicensedServiceOperationExecutor"/> instead, which never saw it. Every Stripe event
/// kept failing with "tenantId is required for service operation authorization" while all of
/// those tests stayed green.
/// </para>
/// </summary>
[TestClass]
public sealed class LicensedWebhookCompositionTests
{
    [TestMethod]
    [DataRow(false, DisplayName = "No licence policy on billing operations")]
    [DataRow(true, DisplayName = "Default entitlement applies to every operation")]
    public async Task SignedWebhook_ThroughLicensedServiceOperations_RunsWithoutTenantOrLicence(bool defaultEntitlement)
    {
        await using var provider = BuildHost(defaultEntitlement);
        var purchaseId = await SeedAwaitingPaymentPurchaseAsync(provider);
        provider.GetRequiredService<FakeGateway>().PurchaseId = purchaseId;

        await using var scope = provider.CreateAsyncScope();
        Assert.IsInstanceOfType<LicensedServiceOperationExecutor>(
            scope.ServiceProvider.GetRequiredService<IServiceOperationExecutor>(),
            "The host must be composed the way production is, or this test proves nothing.");

        var result = await scope.ServiceProvider
            .GetRequiredService<IBillingWebhookProcessor>()
            .ProcessAsync("stripe", Webhook());

        Assert.AreEqual(BillingWebhookProcessingOutcomes.Processed, result.Outcome);
        Assert.AreEqual(BillingPurchaseStatuses.Paid, result.PurchaseStatus);

        // The host's own paid-purchase handler runs inside ProcessAsync, so inside the scope too,
        // and calls an operation that demands an entitlement. A webhook has no licence subject.
        Assert.IsTrue(provider.GetRequiredService<PaidPurchaseProbe>().Ran);

        // Nothing on the webhook path asked for authorization; the scope exempted it.
        Assert.AreEqual(0, provider.GetRequiredService<DenyingAuthorizer>().Calls);

        var stored = await new BillingProviderEventService(provider.GetRequiredService<IBillingStore>())
            .GetEventAsync("stripe", "evt_licensed_host", CancellationToken.None);
        Assert.AreEqual(BillingProviderEventStatuses.Processed, stored?.Status);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "No licence policy on billing operations")]
    [DataRow(true, DisplayName = "Default entitlement applies to every operation")]
    public async Task AfterTheWebhook_TheSameScopedServices_AreCheckedAgain(bool defaultEntitlement)
    {
        await using var provider = BuildHost(defaultEntitlement);
        var purchaseId = await SeedAwaitingPaymentPurchaseAsync(provider);
        provider.GetRequiredService<FakeGateway>().PurchaseId = purchaseId;

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<IBillingWebhookProcessor>()
            .ProcessAsync("stripe", Webhook());

        // Same request scope, same executor, but the system scope has closed: an anonymous caller
        // with no tenant must be refused again, by the licence check where one applies and by
        // authorization otherwise.
        var events = scope.ServiceProvider.GetRequiredService<IBillingProviderEventService>();
        if (defaultEntitlement)
            await Assert.ThrowsExactlyAsync<LicensingException>(() => events.ListEventsAsync());
        else
            await Assert.ThrowsExactlyAsync<AccessControlException>(() => events.ListEventsAsync());

        Assert.IsFalse(scope.ServiceProvider.GetRequiredService<IServiceOperationSystemContext>().IsActive);
    }

    private static ServiceProvider BuildHost(bool defaultEntitlement)
    {
        var settings = new Dictionary<string, string?>();
        if (defaultEntitlement)
            settings[$"{LicensingOptions.SectionName}:ServiceOperations:DefaultEntitlement"] = "app:use";

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddIBeamBillingServices(configuration);
        services.AddIBeamLicensingServices(configuration);
        services.AddIBeamLicensedServiceOperations();

        // A host with authorization switched on. It refuses everything, so any demand that
        // reaches it on the webhook path fails the test.
        services.AddSingleton<DenyingAuthorizer>();
        services.AddSingleton<IServiceOperationAuthorizer>(sp => sp.GetRequiredService<DenyingAuthorizer>());

        services.AddSingleton<FakeGateway>();
        services.AddSingleton<IBillingCheckoutGateway>(sp => sp.GetRequiredService<FakeGateway>());
        services.AddSingleton<PaidPurchaseProbe>();
        services.AddScoped<LicensedHostOperations>();
        services.AddScoped<IBillingPaidPurchaseHandler, HostPaidPurchaseHandler>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private static async Task<Guid> SeedAwaitingPaymentPurchaseAsync(IServiceProvider provider)
    {
        // Seeded straight into the host's store, outside service-operation checks: the purchase
        // being there is the precondition, not what is under test.
        var purchases = new BillingPurchaseService(provider.GetRequiredService<IBillingPurchaseStore>());
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
                ProviderEventId = "checkout-created:cs_licensed",
                EventType = "checkout.session.created",
                Status = BillingPurchaseStatuses.AwaitingPayment,
                ProviderCheckoutSessionId = "cs_licensed"
            });
        return purchase.PurchaseId;
    }

    private static BillingWebhookRequest Webhook()
        => BillingWebhookRequest.Create(
            Encoding.UTF8.GetBytes("{\"verified-by\":\"provider-adapter\"}"),
            new Dictionary<string, string> { ["provider-signature"] = "opaque" });

    private sealed class DenyingAuthorizer : IServiceOperationAuthorizer
    {
        private int _calls;

        public int Calls => _calls;

        public Task<ServiceOperationAuthorizationResult> AuthorizeAsync(
            ServiceOperationAuthorizationRequest request,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(ServiceOperationAuthorizationResult.Deny(request.OperationName, "test host denies everything"));
        }
    }

    private sealed class PaidPurchaseProbe
    {
        public bool Ran { get; set; }
    }

    [IBeamOperation("host.purchases")]
    private sealed class LicensedHostOperations
    {
        private readonly IServiceOperationExecutor _operations;
        private readonly PaidPurchaseProbe _probe;

        public LicensedHostOperations(IServiceOperationExecutor operations, PaidPurchaseProbe probe)
        {
            _operations = operations;
            _probe = probe;
        }

        [IBeamOperation("host.purchases.fulfil")]
        [IBeamRequiresEntitlement("host:fulfil")]
        public Task FulfilAsync(CancellationToken ct)
            => _operations.ExecuteAsync(
                this,
                _ =>
                {
                    _probe.Ran = true;
                    return Task.CompletedTask;
                },
                ct: ct);
    }

    private sealed class HostPaidPurchaseHandler(LicensedHostOperations operations) : IBillingPaidPurchaseHandler
    {
        public Task HandlePaidPurchaseAsync(BillingPurchaseInfo purchase, CancellationToken ct = default)
            => operations.FulfilAsync(ct);
    }

    private sealed class FakeGateway : IBillingCheckoutGateway
    {
        public Guid PurchaseId { get; set; }

        public string ProviderName => "stripe";

        public Task<BillingVerifiedWebhookInfo> VerifyWebhookAsync(BillingWebhookRequest request, CancellationToken ct = default)
            => Task.FromResult(BillingVerifiedWebhookInfo.Create(
                ProviderName,
                "evt_licensed_host",
                BillingCommerceEventTypes.PaymentSucceeded,
                DateTimeOffset.UtcNow.AddMinutes(1),
                providerCustomerId: "cus_licensed",
                providerSubscriptionId: "sub_licensed",
                providerCheckoutSessionId: "cs_licensed",
                metadata: new Dictionary<string, string> { ["purchaseId"] = PurchaseId.ToString("D") }));

        public Task<BillingCheckoutSessionInfo> CreateCheckoutSessionAsync(CreateBillingCheckoutSessionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<BillingCheckoutSessionInfo?> GetCheckoutSessionAsync(string checkoutSessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<BillingCustomerPortalSessionInfo> CreateCustomerPortalSessionAsync(CreateBillingCustomerPortalSessionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
