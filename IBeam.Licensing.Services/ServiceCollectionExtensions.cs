using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IBeam.AccessControl;
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
        services.TryAddScoped<ILicenseRuntimeContextService, LicenseRuntimeContextService>();

        return services;
    }

    public static IServiceCollection AddIBeamLicensedServiceOperations(this IServiceCollection services)
    {
        services.TryAddScoped<ILicenseSubjectResolver, ClaimsPrincipalLicenseSubjectResolver>();

        // Scoped, and TryAdd so a host (or AddIBeamServicePolicies) that already registered it keeps
        // its registration: the webhook processor and this executor must share one instance per
        // request, or a scope opened by one is never seen by the other.
        services.TryAddScoped<IServiceOperationSystemContext, ServiceOperationSystemContext>();
        services.RemoveAll<IServiceOperationExecutor>();
        services.AddScoped<IServiceOperationExecutor, LicensedServiceOperationExecutor>();
        return services;
    }
}
