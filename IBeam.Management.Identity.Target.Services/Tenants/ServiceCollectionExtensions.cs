using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Tenants;

public static class ManagementTenantDirectoryServiceExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementTenantDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddOptions<ManagementTenantDirectoryOptions>().Bind(configuration.GetSection(ManagementTenantDirectoryOptions.SectionName))
            .Validate(x => x.IsValid(), "A dedicated 32-byte directory protection key and bounded lifetime/timeout are required.").ValidateOnStart();
        services.TryAddScoped<ManagementTenantDirectoryService>();
        services.TryAddScoped<IManagementTenantDirectoryService>(p => p.GetRequiredService<ManagementTenantDirectoryService>());
        services.TryAddScoped<IManagementTenantDirectoryQuery>(p => p.GetRequiredService<ManagementTenantDirectoryService>());
        return services;
    }
}
