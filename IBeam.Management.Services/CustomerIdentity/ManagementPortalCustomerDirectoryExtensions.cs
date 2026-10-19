using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Services.CustomerIdentity;

public static class ManagementPortalCustomerDirectoryExtensions
{
    /// <summary>Explicit opt-in. Register a delegated actor provider (and its signing/audit dependencies) separately.</summary>
    public static IServiceCollection AddIBeamManagementPortalCustomerDirectories(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementCustomerIdentity(configuration);
        services.AddOptions<ManagementPortalCustomerDirectoryOptions>().Bind(configuration.GetSection(ManagementPortalCustomerDirectoryOptions.SectionName))
            .Validate(x => x.IsValid(), "Bounded portal customer directory settings and a separate 32-byte protection key are required.").ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IManagementCustomerIdentityConnectionResolver, ManagementCustomerIdentityConnectionResolver>();
        services.TryAddScoped<IManagementCustomerIdentityClient, ManagementCustomerIdentityClient>();
        services.TryAddScoped<IManagementPortalIdentityOperationService, ManagementPortalIdentityOperationService>();
        services.TryAddScoped<IManagementPortalCustomerDirectory<ManagementCustomerTenantDto>, ManagementPortalCustomerDirectory<ManagementCustomerTenantDto>>();
        services.TryAddScoped<IManagementPortalCustomerDirectory<ManagementCustomerUserDto>, ManagementPortalCustomerDirectory<ManagementCustomerUserDto>>();
        services.TryAddScoped<IManagementPortalCustomerDirectory<ManagementCustomerInvitationDto>, ManagementPortalCustomerDirectory<ManagementCustomerInvitationDto>>();
        return services;
    }
}
