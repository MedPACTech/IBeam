using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Services.CustomerIdentity;
public static class ManagementPortalEnterpriseOnboardingExtensions
{
    public static IServiceCollection AddIBeamManagementPortalEnterpriseOnboarding(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementPortalTenantAdministration(configuration);
        services.TryAddScoped<IManagementPortalEnterpriseOnboardingService, ManagementPortalEnterpriseOnboardingService>(); return services;
    }
}
