using IBeam.Api.Models;
using IBeam.Credits;
using IBeam.Credits.Api;
using IBeam.Credits.Services;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Credits;

[TestClass]
public sealed class CreditApiTests
{
    private static readonly Guid TenantId = Guid.Parse("764a1764-77f9-43cd-9beb-932f48aaeb7d");
    private static readonly LicenseSubject Subject = new(LicenseSubjectTypes.User, "user-1");

    [TestMethod]
    public async Task RuntimeController_ReturnsGuidanceOnlyBalanceSummary()
    {
        var fixture = await CreateFixtureAsync(100);
        await fixture.Reservations.ReserveAsync(TenantId, new ReserveCreditsRequest
        {
            CreditAccountId = fixture.CreditAccountId,
            BucketKey = "ai-chat",
            EstimatedAmount = 10,
            MaxAmount = 40
        });
        var controller = CreateRuntimeController(fixture.Summaries);

        var response = AssertOk<CreditRuntimeSummaryInfo>(
            await controller.GetRuntimeSummaryAsync(
                TenantId,
                new GetCreditRuntimeSummaryRequest
                {
                    CreditAccountId = fixture.CreditAccountId,
                    BucketKeys = ["ai-chat"]
                },
                CancellationToken.None));

        Assert.IsNotNull(response.Data);
        Assert.IsTrue(response.Data.GuidanceOnly);
        Assert.AreEqual(40, response.Data.Balances[0].ActiveReserved);
        Assert.AreEqual(60, response.Data.Balances[0].AvailableAfterReservations);
    }

    [TestMethod]
    public async Task AdminController_ListsLedgerEntriesAndReservationsWithFilters()
    {
        var fixture = await CreateFixtureAsync(100);
        await fixture.Reservations.ReserveAsync(TenantId, new ReserveCreditsRequest
        {
            CreditAccountId = fixture.CreditAccountId,
            BucketKey = "ai-chat",
            EstimatedAmount = 5,
            MaxAmount = 10
        });
        var controller = CreateAdminController(fixture.Summaries);

        var ledger = AssertOk<IReadOnlyList<CreditLedgerEntryInfo>>(
            await controller.ListLedgerEntriesAsync(TenantId, fixture.CreditAccountId, "ai-chat", CancellationToken.None));
        var reservations = AssertOk<IReadOnlyList<CreditReservationInfo>>(
            await controller.ListReservationsAsync(TenantId, fixture.CreditAccountId, "ai-chat", CancellationToken.None));

        Assert.IsNotNull(ledger.Data);
        Assert.IsNotNull(reservations.Data);
        Assert.HasCount(1, ledger.Data);
        Assert.HasCount(1, reservations.Data);
    }

    [TestMethod]
    public async Task BootstrapController_ReturnsLicenseRuntimeContextWithCreditSummary()
    {
        var fixture = await CreateFixtureAsync(100);
        await fixture.Licenses.GrantLicenseAsync(TenantId, new GrantTenantLicenseRequest
        {
            PlanKey = "test",
            Entitlements = ["ai:chat"]
        });
        var controller = CreateBootstrapController(fixture.LicenseRuntime, fixture.Summaries);

        var response = AssertOk<CreditBootstrapInfo>(
            await controller.GetBootstrapAsync(
                TenantId,
                new GetCreditBootstrapRequest
                {
                    License = new GetLicenseRuntimeContextRequest { Subject = Subject },
                    Credits = new GetCreditRuntimeSummaryRequest
                    {
                        CreditAccountId = fixture.CreditAccountId,
                        BucketKeys = ["ai-chat"]
                    }
                },
                CancellationToken.None));

        Assert.IsNotNull(response.Data);
        Assert.AreEqual(LicenseRuntimeContextStatuses.Active, response.Data.License.Status);
        Assert.IsTrue(response.Data.CreditsAreGuidanceOnly);
        Assert.AreEqual(100, response.Data.Credits?.Balances[0].AvailableAfterReservations);
    }

    [TestMethod]
    public void AddIBeamCreditsApi_RegistersServicesAndControllers()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddIBeamCreditsApi(configuration);
        using var provider = services.BuildServiceProvider();
        var parts = provider.GetRequiredService<ApplicationPartManager>();

        Assert.IsNotNull(provider.GetRequiredService<ICreditBalanceSummaryService>());
        Assert.IsNotNull(provider.GetRequiredService<ILicenseRuntimeContextService>());
        Assert.IsTrue(parts.ApplicationParts.Any(x => x.Name == typeof(CreditRuntimeController).Assembly.GetName().Name));
    }

    private static async Task<Fixture> CreateFixtureAsync(decimal credits)
    {
        var creditStore = new InMemoryCreditStore();
        var reservations = new CreditReservationService(creditStore);
        var summaries = new CreditBalanceSummaryService(creditStore);
        var creditAccountId = Guid.NewGuid();
        if (credits > 0)
        {
            var grant = CreditGrantInfo.Create(TenantId, creditAccountId, "ai-chat", credits, startsUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
            await creditStore.AppendLedgerEntryAsync(grant.ToLedgerEntry());
        }

        var licensingStore = new InMemoryLicensingStore();
        var licenses = new TenantLicenseService(licensingStore, new EmptyPlanCatalogProvider());
        var licenseRuntime = new LicenseRuntimeContextService(licensingStore);
        return new Fixture(reservations, summaries, licenses, licenseRuntime, creditAccountId);
    }

    private static CreditRuntimeController CreateRuntimeController(ICreditBalanceSummaryService summaries)
    {
        var controller = new CreditRuntimeController(summaries);
        controller.ControllerContext.HttpContext = new DefaultHttpContext();
        return controller;
    }

    private static CreditAdminController CreateAdminController(ICreditBalanceSummaryService summaries)
    {
        var controller = new CreditAdminController(summaries);
        controller.ControllerContext.HttpContext = new DefaultHttpContext();
        return controller;
    }

    private static CreditBootstrapController CreateBootstrapController(
        ILicenseRuntimeContextService licenseRuntime,
        ICreditBalanceSummaryService summaries)
    {
        var controller = new CreditBootstrapController(licenseRuntime, summaries);
        controller.ControllerContext.HttpContext = new DefaultHttpContext();
        return controller;
    }

    private static ApiResponse<T> AssertOk<T>(IActionResult result)
    {
        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        var response = ok.Value as ApiResponse<T>;
        Assert.IsNotNull(response);
        Assert.IsTrue(response.Success);
        return response;
    }

    private sealed record Fixture(
        CreditReservationService Reservations,
        CreditBalanceSummaryService Summaries,
        TenantLicenseService Licenses,
        LicenseRuntimeContextService LicenseRuntime,
        Guid CreditAccountId);

    private sealed class EmptyPlanCatalogProvider : ILicensePlanCatalogProvider
    {
        public Task<IReadOnlyList<LicensePlanInfo>> ListPlansAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LicensePlanInfo>>([]);

        public Task<LicensePlanInfo?> GetPlanAsync(string planKey, CancellationToken ct = default)
            => Task.FromResult<LicensePlanInfo?>(null);
    }
}
