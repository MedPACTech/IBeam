using IBeam.Credits.Services;
using IBeam.Licensing.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace IBeam.Credits.Api;

public static class CreditsApiServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamCreditsApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamCreditServices(configuration);
        services.AddIBeamLicensingServices(configuration);
        services
            .AddControllers()
            .AddApplicationPart(typeof(CreditRuntimeController).GetTypeInfo().Assembly);
        return services;
    }
}
