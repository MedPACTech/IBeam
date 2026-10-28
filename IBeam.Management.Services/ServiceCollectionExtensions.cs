using IBeam.Management;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Services;

public static class ManagementServiceCollectionExtensions
{
    /// <summary>The host must provide a trusted IManagementPrincipalProvider.</summary>
    public static IServiceCollection AddIBeamManagementServices(this IServiceCollection services)
    {
        services.AddIBeamManagementOperationRegistry();
        services.AddIBeamServiceAuditing();
        services.TryAddScoped<IManagementIdentityService, ManagementIdentityService>();
        services.TryAddScoped<IManagementDiagnosticsInfoService, ManagementDiagnosticsInfoService>();
        services.AddOptions<ManagementDiagnosticsOptions>()
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApplicationKey) && o.ApplicationKey.Length <= 100,
                "A management diagnostics application key (maximum 100 characters) is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Environment) && o.Environment.Length <= 100,
                "A management diagnostics environment (maximum 100 characters) is required.");
        return services;
    }
}
