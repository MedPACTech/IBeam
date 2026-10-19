using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Operations;

public static class ManagementIdentityOperationServiceExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementIdentityOperations(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddOptions<ManagementIdentityOperationOptions>().Bind(configuration.GetSection(ManagementIdentityOperationOptions.SectionName))
            .Validate(x => x.IsValid(), "A stable 32-byte operation fingerprint key and bounded retention/lease/timeout/attempt settings are required.").ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IManagementIdentityOperationService, ManagementIdentityOperationService>();
        return services;
    }
}
