using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Services.CustomerIdentity;

public static class ManagementPortalCustomerProvisioningExtensions
{
    public static IServiceCollection AddIBeamManagementPortalCustomerProvisioning(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementPortalTenantAdministration(configuration); // Shared bounded mutation/reference infrastructure.
        services.TryAddScoped<IManagementPortalCustomerProvisioningService, ManagementPortalCustomerProvisioningService>();
        return services;
    }
}
