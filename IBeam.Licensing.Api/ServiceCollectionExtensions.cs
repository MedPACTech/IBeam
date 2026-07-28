using IBeam.Licensing.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace IBeam.Licensing.Api;

public static class LicensingApiServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamLicensingApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamLicensingServices(configuration);
        services
            .AddControllers()
            .AddApplicationPart(typeof(LicensingRuntimeController).GetTypeInfo().Assembly);
        return services;
    }
}
