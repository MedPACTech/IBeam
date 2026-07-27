using IBeam.Api.Models;
using IBeam.Billing;
using IBeam.Billing.Api;
using IBeam.Billing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingApiTests
{
    private static readonly Guid TenantId = Guid.Parse("4e50a835-c29f-4ecd-ae50-877f50406027");

    [TestMethod]
    public async Task AdminController_ListCustomers_DelegatesToBillingService()
    {
        var store = new InMemoryBillingStore();
        var customers = new BillingCustomerService(store);
        await customers.CreateCustomerAsync(
            TenantId,
            new CreateBillingCustomerRequest { DisplayName = "Hubbsly Billing", BillingMode = BillingModes.AnnualContract });
        var controller = CreateAdminController(store);

        var result = await controller.ListCustomersAsync(TenantId, CancellationToken.None);

        var response = AssertOk<IReadOnlyList<BillingCustomerInfo>>(result);
        Assert.IsNotNull(response.Data);
        var customerList = response.Data;
        Assert.HasCount(1, customerList);
        Assert.AreEqual("Hubbsly Billing", customerList[0].DisplayName);
    }

    [TestMethod]
    public async Task ProviderEventsController_RecordEvent_DelegatesToIdempotentService()
    {
        var controller = CreateProviderEventsController(new InMemoryBillingStore());
        var request = new RecordBillingProviderEventRequest
        {
            ProviderName = "stripe",
            ProviderEventId = "evt_123",
            EventType = "invoice.paid",
            TenantId = TenantId
        };

        var first = AssertOk<BillingProviderEventInfo>(await controller.RecordEventAsync(request, CancellationToken.None));
        var second = AssertOk<BillingProviderEventInfo>(await controller.RecordEventAsync(request, CancellationToken.None));

        Assert.IsNotNull(first.Data);
        Assert.IsNotNull(second.Data);
        Assert.AreEqual(first.Data.BillingProviderEventId, second.Data.BillingProviderEventId);
        Assert.AreEqual("stripe:evt_123", first.Data.IdempotencyKey);
    }

    [TestMethod]
    public void AddIBeamBillingApi_RegistersServicesAndControllers()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddIBeamBillingApi(configuration);
        using var provider = services.BuildServiceProvider();
        var parts = provider.GetRequiredService<ApplicationPartManager>();

        Assert.IsNotNull(provider.GetRequiredService<IBillingCustomerService>());
        Assert.IsTrue(parts.ApplicationParts.Any(x => x.Name == typeof(BillingAdminController).Assembly.GetName().Name));
    }

    private static BillingAdminController CreateAdminController(IBillingStore store)
    {
        var controller = new BillingAdminController(
            new BillingCustomerService(store),
            new BillingSubscriptionService(store),
            new BillingInvoiceService(store),
            new BillingProviderEventService(store));
        controller.ControllerContext.HttpContext = new DefaultHttpContext();
        return controller;
    }

    private static BillingProviderEventsController CreateProviderEventsController(IBillingStore store)
    {
        var controller = new BillingProviderEventsController(new BillingProviderEventService(store));
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
}
