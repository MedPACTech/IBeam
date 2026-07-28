using IBeam.Billing.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace IBeam.Billing.Api;

public static class BillingApiServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamBillingApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamBillingServices(configuration);
        services
            .AddControllers()
            .AddApplicationPart(typeof(BillingAdminController).GetTypeInfo().Assembly);
        return services;
    }
}
