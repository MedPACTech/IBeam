using IBeam.Identity.Services.Tenants;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Ownership;

public static class ManagementCustomerOwnershipExtensions
{
    public static IServiceCollection AddIBeamManagementCustomerOwnership(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementIdentityOperations(configuration);
        services.AddIBeamIdentityTenantOwnershipAdministration(configuration);
        services.TryAddScoped<IManagementCustomerOwnershipService, ManagementCustomerOwnershipService>();
        return services;
    }
}
