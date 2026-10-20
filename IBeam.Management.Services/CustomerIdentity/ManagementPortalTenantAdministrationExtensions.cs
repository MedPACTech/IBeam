using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Services.CustomerIdentity;

public static class ManagementPortalTenantAdministrationExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementPortalTenantAdministration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementPortalCustomerDirectories(configuration);
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddOptions<PortalCustomerMutationOptions>().Bind(configuration.GetSection(PortalCustomerMutationOptions.SectionName))
            .Validate(x => x.IsValid(), "Bounded mutation attempts and reserved live-authorization budgets are required.").ValidateOnStart();
        services.TryAddScoped<PortalMutationRuntime>(); services.TryAddSingleton<PortalMutationReferences>();
        services.TryAddScoped<IManagementPortalTenantAdministrationService, ManagementPortalTenantAdministrationService>();
        services.TryAddScoped<IManagementPortalMutationOperationService, ManagementPortalMutationOperationService>();
        return services;
    }
}
