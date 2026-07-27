using IBeam.Licensing;
using IBeam.Licensing.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace IBeam.Tests.Licensing;

[TestClass]
public sealed class LicensingServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("225925cc-995e-4584-a63b-4f2cb4f38f6f");

    [TestMethod]
    public async Task PlanCatalog_ReturnsConfiguredPlans()
    {
        var provider = CreatePlanCatalog();

        var plans = await provider.ListPlansAsync();

        Assert.HasCount(1, plans);
        Assert.AreEqual("hubbsly-work", plans[0].Key);
        Assert.AreEqual("hubbsly", plans[0].ProductKey);
        Assert.AreEqual(LicensePlanClassifications.Tenant, plans[0].Classification);
        Assert.AreEqual(2, plans[0].Level);
        Assert.AreEqual(2, plans[0].DefaultSeatLimit);
        CollectionAssert.Contains(plans[0].Entitlements.ToList(), "work:cards:create");
        Assert.AreEqual(2, plans[0].Limits["Seats"]);
        Assert.HasCount(1, plans[0].DefaultCreditGrants);
        Assert.AreEqual("ai-chat", plans[0].DefaultCreditGrants[0].BucketKey);
        Assert.AreEqual(500, plans[0].DefaultCreditGrants[0].Amount);
        Assert.HasCount(1, plans[0].ProviderPrices);
        Assert.AreEqual("stripe", plans[0].ProviderPrices[0].ProviderName);
        Assert.AreEqual("price_hubbsly_work_monthly", plans[0].ProviderPrices[0].PriceId);
    }

    [TestMethod]
    public async Task PlanCatalog_ReturnsConfiguredProducts()
    {
        var provider = CreatePlanCatalog();

        var products = await provider.ListProductsAsync();
        var product = await provider.GetProductAsync(" HUBBSLY ");

        Assert.HasCount(1, products);
        Assert.IsNotNull(product);
        Assert.AreEqual("hubbsly", product.Key);
        Assert.AreEqual("Hubbsly", product.DisplayName);
        Assert.AreEqual("work", product.Metadata["module"]);
    }

    [TestMethod]
    public void LicensePlanInfo_PreservesLegacyConstructorDefaults()
    {
        var plan = new LicensePlanInfo(
            "legacy",
            "Legacy",
            null,
            ["legacy:use"],
            new Dictionary<string, int>(),
            new Dictionary<string, string>());

        Assert.AreEqual("legacy", plan.Key);
        Assert.IsTrue(plan.IsConfigured);
        Assert.IsNull(plan.ProductKey);
        Assert.AreEqual(LicensePlanClassifications.Tenant, plan.Classification);
        Assert.IsNull(plan.DefaultSeatLimit);
        Assert.HasCount(0, plan.DefaultCreditGrants);
        Assert.HasCount(0, plan.ProviderPrices);
    }

    [TestMethod]
    public async Task PlanCatalog_NormalizesRichCatalogFields()
    {
        var provider = new ConfigurationLicensePlanCatalogProvider(Options.Create(new LicensingOptions
        {
            Products =
            [
                new LicenseProductOptions
                {
                    Key = " hubbsly ",
                    DisplayName = " Hubbsly ",
                    Metadata = new Dictionary<string, string> { [" module "] = " work " }
                },
                new LicenseProductOptions { Key = "HUBBSLY", DisplayName = "Duplicate" }
            ],
            Plans =
            [
                new LicensePlanOptions
                {
                    Key = " enterprise ",
                    ProductKey = " hubbsly ",
                    Classification = " ENTERPRISE ",
                    Level = 3,
                    DefaultSeatLimit = 25,
                    Entitlements = [" work:cards:create ", "WORK:CARDS:CREATE", " work:cards:update "],
                    Limits = new Dictionary<string, int> { [" Seats "] = 25 },
                    DefaultCreditGrants =
                    [
                        new LicenseCreditGrantOptions
                        {
                            BucketKey = " ai-chat ",
                            Amount = 2500,
                            Period = " monthly ",
                            Metadata = new Dictionary<string, string> { [" rollover "] = " false " }
                        },
                        new LicenseCreditGrantOptions { BucketKey = "ignored", Amount = 0 }
                    ],
                    ProviderPrices =
                    [
                        new LicenseProviderPriceOptions
                        {
                            ProviderName = " stripe ",
                            PriceId = " price_enterprise ",
                            Currency = " usd ",
                            UnitAmount = 499m,
                            BillingPeriod = " monthly "
                        },
                        new LicenseProviderPriceOptions { ProviderName = "stripe" }
                    ],
                    Metadata = new Dictionary<string, string> { [" market "] = " healthcare " }
                }
            ]
        }));

        var products = await provider.ListProductsAsync();
        var plans = await provider.ListPlansAsync();

        Assert.HasCount(1, products);
        Assert.AreEqual("hubbsly", products[0].Key);
        Assert.AreEqual("work", products[0].Metadata["module"]);
        Assert.HasCount(1, plans);
        Assert.AreEqual("enterprise", plans[0].Key);
        Assert.AreEqual("hubbsly", plans[0].ProductKey);
        Assert.AreEqual(LicensePlanClassifications.Enterprise, plans[0].Classification);
        Assert.AreEqual(3, plans[0].Level);
        Assert.AreEqual(25, plans[0].DefaultSeatLimit);
        CollectionAssert.AreEqual(
            new[] { "work:cards:create", "work:cards:update" },
            plans[0].Entitlements.ToArray());
        Assert.AreEqual(25, plans[0].Limits["Seats"]);
        Assert.AreEqual("healthcare", plans[0].Metadata["market"]);
        Assert.HasCount(1, plans[0].DefaultCreditGrants);
        Assert.AreEqual("ai-chat", plans[0].DefaultCreditGrants[0].BucketKey);
        Assert.AreEqual("false", plans[0].DefaultCreditGrants[0].Metadata["rollover"]);
        Assert.HasCount(1, plans[0].ProviderPrices);
        Assert.AreEqual("USD", plans[0].ProviderPrices[0].Currency);
    }

    [TestMethod]
    public async Task GrantLicense_MergesPlanEntitlementsLimitsAndMetadata()
    {
        var fixture = CreateFixture();

        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "hubbsly-work",
                Entitlements = ["mcp:tools"],
                Limits = new Dictionary<string, int> { ["McpCallsPerMonth"] = 1000 },
                Metadata = new Dictionary<string, string> { ["contractNumber"] = "C-100" }
            });

        Assert.AreEqual("hubbsly-work", license.PlanKey);
        Assert.AreEqual(2, license.SeatLimit);
        CollectionAssert.Contains(license.Entitlements.ToList(), "work:cards:create");
        CollectionAssert.Contains(license.Entitlements.ToList(), "mcp:tools");
        Assert.AreEqual(1000, license.Limits["McpCallsPerMonth"]);
        Assert.AreEqual("C-100", license.Metadata["contractNumber"]);
    }

    [TestMethod]
    public async Task GrantLicense_DistinguishesCommercialStatusFromRuntimeStatus()
    {
        var fixture = CreateFixture();

        var manual = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "manual-work",
                Status = LicenseStatuses.Active,
                Entitlements = ["work:cards:create"]
            });

        var paid = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "paid-work",
                Status = LicenseStatuses.Active,
                Entitlements = ["work:cards:create"],
                ProviderName = "stripe",
                ProviderSubscriptionId = "sub_123"
            });

        var trial = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "trial-work",
                Status = LicenseStatuses.Trialing,
                Entitlements = ["work:cards:create"]
            });

        Assert.AreEqual(LicenseStatuses.Active, manual.Status);
        Assert.AreEqual(LicenseCommercialStatuses.Manual, manual.CommercialStatus);
        Assert.AreEqual(LicenseCommercialStatuses.Paid, paid.CommercialStatus);
        Assert.AreEqual(LicenseStatuses.Trialing, trial.Status);
        Assert.AreEqual(LicenseCommercialStatuses.Trial, trial.CommercialStatus);
    }

    [TestMethod]
    public async Task GrantLicenseAsync_UsesServiceOperationExecutor()
    {
        var store = new InMemoryLicensingStore();
        var executor = new RecordingServiceOperationExecutor();
        var licenses = new TenantLicenseService(store, CreatePlanCatalog(), executor);

        await licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(nameof(TenantLicenseService.GrantLicenseAsync), executor.Calls[0].CallerMemberName);
        Assert.AreEqual(TenantId, executor.Calls[0].Options?.TenantId);
    }

    [TestMethod]
    public async Task AssignSeatAsync_EnforcesSeatLimit()
    {
        var fixture = CreateFixture();
        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1") });

        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = new LicenseSubject(LicenseSubjectTypes.User, "user-2") });

        await Assert.ThrowsExactlyAsync<LicensingException>(() =>
            fixture.Assignments.AssignSeatAsync(
                TenantId,
                license.LicenseId,
                new AssignLicenseSeatRequest { Subject = new LicenseSubject(LicenseSubjectTypes.User, "user-3") }));
    }

    [TestMethod]
    public async Task AssignSeatAsync_UsesServiceOperationExecutor()
    {
        var store = new InMemoryLicensingStore();
        var license = await new TenantLicenseService(store, CreatePlanCatalog())
            .GrantLicenseAsync(TenantId, new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });
        var executor = new RecordingServiceOperationExecutor();
        var assignments = new LicenseSeatAssignmentService(store, executor);

        await assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1") });

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(nameof(LicenseSeatAssignmentService.AssignSeatAsync), executor.Calls[0].CallerMemberName);
        Assert.AreEqual(TenantId, executor.Calls[0].Options?.TenantId);
        Assert.AreEqual(license.LicenseId, executor.Calls[0].Options?.EntityId);
    }

    [TestMethod]
    public async Task AuthorizeAsync_AllowsAssignedSubjectWithEntitlement()
    {
        var fixture = CreateFixture();
        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        var subject = new LicenseSubject(LicenseSubjectTypes.Agent, "codex");
        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = subject });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsTrue(result.Allowed);
        Assert.AreEqual(license.LicenseId, result.LicenseId);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesMissingEntitlement()
    {
        var fixture = CreateFixture();
        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        var subject = new LicenseSubject(LicenseSubjectTypes.Agent, "codex");
        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = subject });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "money:close:update");

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesUnassignedSubjectWhenLicenseHasSeatLimit()
    {
        var fixture = CreateFixture();
        await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            new LicenseSubject(LicenseSubjectTypes.Agent, "codex"),
            "work:cards:create");

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesExpiredLicense()
    {
        var fixture = CreateFixture();
        var subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1");
        await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "expired-work",
                Entitlements = ["work:cards:create"],
                StartsUtc = DateTimeOffset.UtcNow.AddDays(-10),
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(-1)
            });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesFutureLicense()
    {
        var fixture = CreateFixture();
        var subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1");
        await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "future-work",
                Entitlements = ["work:cards:create"],
                StartsUtc = DateTimeOffset.UtcNow.AddDays(1),
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsFalse(result.Allowed);
        StringAssert.Contains(result.Reason, LicenseRuntimeStatuses.NotStarted);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesSuspendedLicense()
    {
        var fixture = CreateFixture();
        var subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1");
        await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "suspended-work",
                Status = LicenseStatuses.Suspended,
                Entitlements = ["work:cards:create"]
            });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsFalse(result.Allowed);
        StringAssert.Contains(result.Reason, LicenseRuntimeStatuses.Suspended);
    }

    [TestMethod]
    public async Task AuthorizeAsync_AllowsGraceLicenseUntilGraceEnds()
    {
        var fixture = CreateFixture();
        var subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1");
        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "grace-work",
                Status = LicenseStatuses.Grace,
                Entitlements = ["work:cards:create"],
                StartsUtc = DateTimeOffset.UtcNow.AddDays(-40),
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(-1),
                GraceEndsUtc = DateTimeOffset.UtcNow.AddDays(6)
            });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsTrue(result.Allowed);
        Assert.AreEqual(license.LicenseId, result.LicenseId);
        Assert.AreEqual(LicenseCommercialStatuses.Grace, license.CommercialStatus);
        Assert.AreEqual(LicenseStatuses.Grace, license.Status);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesGraceLicenseAfterGraceEnds()
    {
        var fixture = CreateFixture();
        var subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1");
        await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "grace-ended-work",
                Status = LicenseStatuses.Grace,
                Entitlements = ["work:cards:create"],
                StartsUtc = DateTimeOffset.UtcNow.AddDays(-40),
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(-10),
                GraceEndsUtc = DateTimeOffset.UtcNow.AddDays(-1)
            });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsFalse(result.Allowed);
        StringAssert.Contains(result.Reason, LicenseRuntimeStatuses.Expired);
    }

    [TestMethod]
    public async Task RevokeLicenseAsync_RemovesAuthorization()
    {
        var fixture = CreateFixture();
        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        var subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1");
        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = subject });

        await fixture.Licenses.RevokeLicenseAsync(TenantId, license.LicenseId, "cancelled");

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task RequireEntitlementAsync_ThrowsWhenDenied()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsExactlyAsync<LicensingException>(() =>
            fixture.Authorizer.RequireEntitlementAsync(
                TenantId,
                new LicenseSubject(LicenseSubjectTypes.User, "user-1"),
                "work:cards:create"));
    }

    private static Fixture CreateFixture()
    {
        var store = new InMemoryLicensingStore();
        var catalog = CreatePlanCatalog();
        var licenses = new TenantLicenseService(store, catalog);
        var assignments = new LicenseSeatAssignmentService(store);
        var authorizer = new LicenseAuthorizer(store);

        return new Fixture(licenses, assignments, authorizer);
    }

    private static ConfigurationLicensePlanCatalogProvider CreatePlanCatalog()
        => new(Options.Create(new LicensingOptions
        {
            Plans =
            [
                new LicensePlanOptions
                {
                    Key = "hubbsly-work",
                    ProductKey = "hubbsly",
                    DisplayName = "Hubbsly Work",
                    Classification = LicensePlanClassifications.Tenant,
                    Level = 2,
                    Entitlements = ["feature:work", "work:cards:create", "work:cards:update"],
                    Limits = new Dictionary<string, int> { ["Seats"] = 2 },
                    DefaultSeatLimit = 2,
                    DefaultCreditGrants =
                    [
                        new LicenseCreditGrantOptions
                        {
                            BucketKey = "ai-chat",
                            Amount = 500,
                            DisplayName = "AI Chat Credits",
                            Period = "monthly"
                        }
                    ],
                    ProviderPrices =
                    [
                        new LicenseProviderPriceOptions
                        {
                            ProviderName = "stripe",
                            PriceId = "price_hubbsly_work_monthly",
                            Currency = "usd",
                            UnitAmount = 49m,
                            BillingPeriod = "monthly"
                        }
                    ],
                    Metadata = new Dictionary<string, string> { ["product"] = "hubbsly" }
                }
            ],
            Products =
            [
                new LicenseProductOptions
                {
                    Key = "hubbsly",
                    DisplayName = "Hubbsly",
                    Metadata = new Dictionary<string, string> { ["module"] = "work" }
                }
            ]
        }));

    private sealed record Fixture(
        TenantLicenseService Licenses,
        LicenseSeatAssignmentService Assignments,
        LicenseAuthorizer Authorizer);

    private sealed class RecordingServiceOperationExecutor : IServiceOperationExecutor
    {
        private readonly List<ServiceOperationCall> _calls = [];

        public IReadOnlyList<ServiceOperationCall> Calls => _calls;

        public async Task ExecuteAsync(
            object serviceInstance,
            Func<CancellationToken, Task> operation,
            ServiceOperationExecutionOptions? options = null,
            CancellationToken ct = default,
            [CallerMemberName] string? callerMemberName = null)
        {
            _calls.Add(new ServiceOperationCall(callerMemberName, options));
            await operation(ct).ConfigureAwait(false);
        }

        public async Task<TResult> ExecuteAsync<TResult>(
            object serviceInstance,
            Func<CancellationToken, Task<TResult>> operation,
            ServiceOperationExecutionOptions? options = null,
            CancellationToken ct = default,
            [CallerMemberName] string? callerMemberName = null)
        {
            _calls.Add(new ServiceOperationCall(callerMemberName, options));
            return await operation(ct).ConfigureAwait(false);
        }

        public void Execute(
            object serviceInstance,
            Action operation,
            ServiceOperationExecutionOptions? options = null,
            [CallerMemberName] string? callerMemberName = null)
        {
            _calls.Add(new ServiceOperationCall(callerMemberName, options));
            operation();
        }

        public TResult Execute<TResult>(
            object serviceInstance,
            Func<TResult> operation,
            ServiceOperationExecutionOptions? options = null,
            [CallerMemberName] string? callerMemberName = null)
        {
            _calls.Add(new ServiceOperationCall(callerMemberName, options));
            return operation();
        }
    }

    private sealed record ServiceOperationCall(
        string? CallerMemberName,
        ServiceOperationExecutionOptions? Options);
}
