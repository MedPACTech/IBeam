using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IBeam.Services.Abstractions;

namespace IBeam.Licensing.Services;

public static class LicensingServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamLicensingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamServicePolicies();
        services.AddIBeamServiceAuditing(configuration);

        services.AddOptions<LicensingOptions>()
            .Bind(configuration.GetSection(LicensingOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.TryAddSingleton<ConfigurationLicensePlanCatalogProvider>();
        services.TryAddSingleton<ILicensePlanCatalogProvider>(
            provider => provider.GetRequiredService<ConfigurationLicensePlanCatalogProvider>());
        services.TryAddSingleton<ILicenseProductCatalogProvider>(
            provider => provider.GetRequiredService<ConfigurationLicensePlanCatalogProvider>());
        services.TryAddSingleton<ILicensingStore, InMemoryLicensingStore>();
        services.TryAddScoped<ITenantLicenseService, TenantLicenseService>();
        services.TryAddScoped<ILicenseSeatAssignmentService, LicenseSeatAssignmentService>();
        services.TryAddScoped<ILicenseSeatPolicyService, LicenseSeatPolicyService>();
        services.TryAddScoped<ILicenseAuthorizer, LicenseAuthorizer>();
        services.TryAddScoped<ILicenseGate, LicenseGate>();

        return services;
    }

    public static IServiceCollection AddIBeamLicensedServiceOperations(this IServiceCollection services)
    {
        services.TryAddScoped<ILicenseSubjectResolver, ClaimsPrincipalLicenseSubjectResolver>();
        services.RemoveAll<IServiceOperationExecutor>();
        services.AddScoped<IServiceOperationExecutor, LicensedServiceOperationExecutor>();
        return services;
    }
}
