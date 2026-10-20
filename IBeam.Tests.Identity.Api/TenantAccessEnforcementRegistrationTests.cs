using IBeam.Identity.Api.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class TenantAccessEnforcementRegistrationTests
{
    [TestMethod]
    public async Task DeclaringStandardEnforcementWithoutACustomerBearerSchemeFailsStartup()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAuthentication(); services.AddIBeamIdentityTenantAccessEnforcementApi("Customer");
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            foreach (var startup in provider.GetServices<IHostedService>()) await startup.StartAsync(default);
        });
        Assert.Contains("registered customer JWT bearer scheme", error.Message);
    }
    [TestMethod]
    public async Task ACustomEventsTypeCannotBypassTheDeclaredStandardValidationCallback()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAuthentication().AddJwtBearer("Customer", x => x.EventsType = typeof(JwtBearerEvents));
        services.AddIBeamIdentityTenantAccessEnforcementApi("Customer");
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            foreach (var startup in provider.GetServices<IHostedService>()) await startup.StartAsync(default);
        });
        Assert.Contains("Custom JWT event types", error.Message);
    }
}
