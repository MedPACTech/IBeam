using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Provisioning;

public static class ManagementCustomerProvisioningServiceExtensions
{
    private sealed class CatalogRegistration;
    public static IServiceCollection AddIBeamManagementAssignableRoleCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(CatalogRegistration))) return services;
        services.AddSingleton<CatalogRegistration>();
        services.AddOptions<ManagementCustomerProvisioningOptions>().Bind(configuration.GetSection(ManagementCustomerProvisioningOptions.SectionName))
            .Validate(x => x.IsValid(), "Provisioning requires explicit policy keys and bounded target-local role-name allowlists.").ValidateOnStart();
        services.AddOptions<ManagementIdentityOperationOptions>();
        services.TryAddScoped<IManagementAssignableRoleCatalogService, ManagementAssignableRoleCatalogService>();
        return services;
    }
    public static IServiceCollection AddIBeamManagementCustomerProvisioning(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementAssignableRoleCatalog(configuration);
        services.AddIBeamManagementIdentityOperations(configuration);
        services.TryAddScoped<IExternalTenantInviteService>(p => p.GetRequiredService<ITenantInviteService>() is IExternalTenantInviteService service && service.SupportsDurableCreation
            ? service : throw new InvalidOperationException("A durable external Identity invitation adapter is required."));
        services.TryAddScoped<IManagementCustomerProvisioningService, ManagementCustomerProvisioningService>();
        services.TryAddScoped<IManagementCustomerInvitationOperationQuery, IBeam.Management.Identity.Target.Services.Invitations.ManagementCustomerInvitationOperationQuery>();
        return services;
    }
}
