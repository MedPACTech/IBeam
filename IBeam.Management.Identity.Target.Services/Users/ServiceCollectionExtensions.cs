using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Users;

public static class ManagementCustomerUserDirectoryExtensions
{
    private static void AddCommon(IServiceCollection services, IConfiguration configuration)
    {
        if (!services.Any(x => x.ServiceType == typeof(ManagementCustomerDirectoryRuntime)))
        {
            services.AddOptions<ManagementCustomerUserDirectoryOptions>().Bind(configuration.GetSection(ManagementCustomerUserDirectoryOptions.SectionName))
                .Validate(x => x.IsValid(), "A dedicated 32-byte user-directory protection key and bounded expiry/timeout are required.").ValidateOnStart();
            services.TryAddScoped<ManagementCustomerDirectoryRuntime>();
            services.TryAddScoped<IManagementCustomerTenantScopeService, ManagementCustomerTenantScopeService>();
        }
    }
    public static IServiceCollection AddIBeamManagementCustomerUserDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        AddCommon(services, configuration);
        services.TryAddScoped<ManagementCustomerUserDirectoryService>();
        services.TryAddScoped<IManagementCustomerUserDirectoryService>(p => p.GetRequiredService<ManagementCustomerUserDirectoryService>());
        services.TryAddScoped<IManagementCustomerUserDirectoryQuery>(p => p.GetRequiredService<ManagementCustomerUserDirectoryService>());
        return services;
    }
    public static IServiceCollection AddIBeamManagementCustomerInvitationDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        AddCommon(services, configuration);
        services.TryAddScoped<IManagementCustomerInvitationDirectoryService, ManagementCustomerInvitationDirectoryService>();
        return services;
    }
}
