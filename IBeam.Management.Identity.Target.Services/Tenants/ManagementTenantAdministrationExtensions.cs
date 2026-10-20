using IBeam.Identity.Interfaces;
using IBeam.Identity.Services.Tenants;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Tenants;

public static class ManagementTenantAdministrationExtensions
{
    public static IServiceCollection AddIBeamManagementTenantAdministration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamIdentityTenantAdministration();
        services.AddIBeamManagementIdentityOperations(configuration);
        services.TryAddScoped<ManagementTenantAdministrationService>();
        services.TryAddScoped<IManagementTenantAdministrationService>(p => p.GetRequiredService<ManagementTenantAdministrationService>());
        services.TryAddScoped<IManagementTenantRenameCapability>(p => p.GetRequiredService<IIdentityTenantAdministrationService>().SupportsConditionalUpdates
            ? p.GetRequiredService<ManagementTenantAdministrationService>() : null!);
        return services;
    }
}
