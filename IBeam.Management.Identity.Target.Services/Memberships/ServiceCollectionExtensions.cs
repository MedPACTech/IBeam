using IBeam.Identity.Services.Tenants;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Management.Identity.Target.Services.Provisioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Memberships;
public static class ManagementCustomerMembershipExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementCustomerMemberships(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddIBeamManagementIdentityOperations(configuration); services.AddIBeamManagementAssignableRoleCatalog(configuration);
        services.AddIBeamIdentityTenantMembershipAdministration();
        services.AddOptions<ManagementCustomerMembershipOptions>().Bind(configuration.GetSection(ManagementCustomerMembershipOptions.SectionName))
            .Validate(x => x.IsValid(), "Immutable target-owned administrator roles are required.").ValidateOnStart();
        services.TryAddScoped<IManagementCustomerMembershipService, ManagementCustomerMembershipService>(); return services;
    }
}
