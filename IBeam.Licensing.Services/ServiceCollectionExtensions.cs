using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Licensing.Services;

public static class LicensingServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamLicensingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LicensingOptions>()
            .Bind(configuration.GetSection(LicensingOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.TryAddSingleton<ILicensePlanCatalogProvider, ConfigurationLicensePlanCatalogProvider>();
        services.TryAddSingleton<ILicensingStore, InMemoryLicensingStore>();
        services.TryAddScoped<ITenantLicenseService, TenantLicenseService>();
        services.TryAddScoped<ILicenseSeatAssignmentService, LicenseSeatAssignmentService>();
        services.TryAddScoped<ILicenseAuthorizer, LicenseAuthorizer>();

        return services;
    }
}
