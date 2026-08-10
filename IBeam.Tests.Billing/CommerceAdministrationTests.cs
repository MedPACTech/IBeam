using System.Security.Claims;
using IBeam.Billing;
using IBeam.Billing.Api;
using IBeam.Billing.Licensing;
using IBeam.Billing.Services;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class CommerceAdministrationTests
{
    [TestMethod]
    public async Task Inspect_RedactsBuyerAndReturnsClaimedLicenseState()
    {
        var fixture = await Fixture.CreateAsync();
        var issued = await fixture.Administration.ResendClaimAsync(
            fixture.TenantId, fixture.PurchaseId, new() { Reason = "Send onboarding" });
        await fixture.Claims.ClaimAsync(new()
        {
            ClaimToken = issued.ClaimToken,
            TenantId = fixture.TenantId,
            UserId = fixture.UserId,
            VerifiedEmail = "buyer@example.test"
        });

        var result = await fixture.Administration.InspectAsync(fixture.TenantId, fixture.PurchaseId);

        Assert.AreEqual("b***@example.test", result.Purchase.BuyerEmail);
        Assert.IsNotNull(result.Claim);
        Assert.IsNotNull(result.License);
        Assert.HasCount(1, result.SeatAssignments);
        Assert.IsFalse(result.Claim.GetType().GetProperties().Any(x => x.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ResendClaim_RotatesTheOneTimeToken()
    {
        var fixture = await Fixture.CreateAsync();
        var first = await fixture.Claims.IssueAsync(fixture.PurchaseId);

        var second = await fixture.Administration.ResendClaimAsync(
            fixture.TenantId, fixture.PurchaseId, new() { Reason = "Buyer deleted the first email" });

        await Assert.ThrowsExactlyAsync<BillingException>(() => fixture.Claims.ClaimAsync(new()
        {
            ClaimToken = first.ClaimToken,
            TenantId = fixture.TenantId,
            UserId = fixture.UserId,
            VerifiedEmail = "buyer@example.test"
        }));
        var claimed = await fixture.Claims.ClaimAsync(new()
        {
            ClaimToken = second.ClaimToken,
            TenantId = fixture.TenantId,
            UserId = fixture.UserId,
            VerifiedEmail = "buyer@example.test"
        });
        Assert.AreEqual(fixture.TenantId, claimed.TenantId);
    }

    [TestMethod]
    public async Task RetryFulfillment_IsIdempotent()
    {
        var fixture = await Fixture.CreateAsync();
        var before = await fixture.Purchases.GetPurchaseAsync(fixture.PurchaseId);

        var retry = await fixture.Administration.RetryFulfillmentAsync(
            fixture.TenantId, fixture.PurchaseId, new() { Reason = "Verify fulfillment after timeout" });

        Assert.AreEqual(BillingPurchaseStatuses.Fulfilled, retry.Status);
        Assert.AreEqual(before!.LicenseKey, retry.LicenseKey);
    }

    [TestMethod]
    public async Task RetryVerifiedEvent_ProcessesFailedEventWithoutDuplicateFulfillment()
    {
        var fixture = await Fixture.CreateAsync(fulfill: false);
        await fixture.Events.RecordEventAsync(new()
        {
            ProviderName = "stripe",
            ProviderEventId = "evt_recover",
            EventType = BillingCommerceEventTypes.PaymentSucceeded,
            Status = BillingProviderEventStatuses.Failed,
            TenantId = fixture.TenantId,
            Metadata = new() { ["purchaseId"] = fixture.PurchaseId.ToString("D") }
        });

        var result = await fixture.Administration.RetryVerifiedEventAsync(
            fixture.TenantId, "stripe", "evt_recover", new() { Reason = "Transient fulfillment error resolved" });
        var storedEvent = await fixture.Events.GetEventAsync("stripe", "evt_recover");
        var purchase = await fixture.Purchases.GetPurchaseAsync(fixture.PurchaseId);

        Assert.AreEqual(BillingWebhookProcessingOutcomes.Processed, result.Outcome);
        Assert.AreEqual(BillingProviderEventStatuses.Processed, storedEvent!.Status);
        Assert.AreEqual(BillingPurchaseStatuses.Fulfilled, purchase!.Status);
        Assert.IsNotNull(purchase.LicenseKey);
    }

    [TestMethod]
    public async Task ManualCorrection_RequiresReasonAndIsIdempotent()
    {
        var fixture = await Fixture.CreateAsync(markPaid: false, fulfill: false);
        var request = new ManualCommerceCorrectionRequest
        {
            Reason = "Processor confirmed settlement",
            IdempotencyKey = "ticket-123",
            Status = BillingPurchaseStatuses.Paid
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Administration.ApplyManualCorrectionAsync(
            fixture.TenantId, fixture.PurchaseId, new() { IdempotencyKey = "ticket-122", Status = BillingPurchaseStatuses.Paid }));
        var first = await fixture.Administration.ApplyManualCorrectionAsync(fixture.TenantId, fixture.PurchaseId, request);
        var retry = await fixture.Administration.ApplyManualCorrectionAsync(fixture.TenantId, fixture.PurchaseId, request);

        Assert.AreEqual(BillingPurchaseStatuses.Fulfilled, first.Status);
        Assert.AreEqual(first.LicenseKey, retry.LicenseKey);
    }

    [TestMethod]
    public async Task Controller_ForbidsAdminFromDifferentTenant()
    {
        var fixture = await Fixture.CreateAsync();
        var controller = new CommerceAdministrationController(fixture.Administration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("tid", Guid.NewGuid().ToString("D")),
                        new Claim(ClaimTypes.Role, "Administrator")
                    ], "test"))
                }
            }
        };

        var result = await controller.InspectAsync(fixture.TenantId, fixture.PurchaseId, CancellationToken.None);

        Assert.IsInstanceOfType<ForbidResult>(result);
    }

    private sealed class Fixture
    {
        private Fixture(
            Guid tenantId,
            Guid userId,
            Guid purchaseId,
            BillingPurchaseService purchases,
            BillingPurchaseClaimService claims,
            BillingProviderEventService events,
            CommerceAdministrationService administration)
        {
            TenantId = tenantId;
            UserId = userId;
            PurchaseId = purchaseId;
            Purchases = purchases;
            Claims = claims;
            Events = events;
            Administration = administration;
        }

        public Guid TenantId { get; }
        public Guid UserId { get; }
        public Guid PurchaseId { get; }
        public BillingPurchaseService Purchases { get; }
        public BillingPurchaseClaimService Claims { get; }
        public BillingProviderEventService Events { get; }
        public CommerceAdministrationService Administration { get; }

        public static async Task<Fixture> CreateAsync(bool markPaid = true, bool fulfill = true)
        {
            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var purchaseStore = new InMemoryBillingPurchaseStore();
            var purchases = new BillingPurchaseService(purchaseStore);
            var pending = await purchases.CreatePendingPurchaseAsync(new()
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
            var purchase = pending;
            if (markPaid)
            {
                purchase = await purchases.ApplyProviderUpdateAsync(pending.PurchaseId, new()
                {
                    ProviderName = "stripe",
                    ProviderEventId = "evt_paid",
                    EventType = BillingCommerceEventTypes.PaymentSucceeded,
                    Status = BillingPurchaseStatuses.Paid,
                    ProviderCustomerId = "cus_123",
                    ProviderSubscriptionId = "sub_123"
                });
            }

            var fulfillment = new BillingPurchaseLicenseFulfillmentService(purchases);
            if (fulfill)
                purchase = await purchases.GetPurchaseAsync((await fulfillment.FulfillAsync(new() { PurchaseId = purchase.PurchaseId })).PurchaseId) ?? purchase;

            var licensingStore = new InMemoryLicensingStore();
            var licenseService = new TenantLicenseService(licensingStore, new FakePlanCatalog());
            var assignments = new LicenseSeatAssignmentService(licensingStore);
            var policies = new LicenseSeatPolicyService(licenseService, assignments);
            var claimStore = new InMemoryBillingPurchaseClaimStore();
            var claims = new BillingPurchaseClaimService(
                purchases, claimStore, policies, Options.Create(new BillingPurchaseClaimOptions()));
            var billingStore = new InMemoryBillingStore();
            var events = new BillingProviderEventService(billingStore);
            var subscriptions = new BillingSubscriptionService(billingStore);
            var bindings = new InMemoryBillingSubscriptionProviderBindingStore();
            var administration = new CommerceAdministrationService(
                purchases, claims, claimStore, subscriptions, bindings, events, billingStore,
                licenseService, assignments, [fulfillment]);
            return new Fixture(tenantId, userId, purchase.PurchaseId, purchases, claims, events, administration);
        }
    }

    private sealed class FakePlanCatalog : ILicensePlanCatalogProvider
    {
        public Task<IReadOnlyList<LicensePlanInfo>> ListPlansAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LicensePlanInfo>>(
                [new LicensePlanInfo("hubbsly-pro", "Hubbsly Pro", null, ["tool:mcp"], new Dictionary<string, int>(), new Dictionary<string, string>())]);

        public async Task<LicensePlanInfo?> GetPlanAsync(string planKey, CancellationToken ct = default)
            => (await ListPlansAsync(ct)).FirstOrDefault(x => string.Equals(x.Key, planKey, StringComparison.OrdinalIgnoreCase));
    }
}
