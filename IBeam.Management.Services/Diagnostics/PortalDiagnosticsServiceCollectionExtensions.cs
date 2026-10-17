using IBeam.Management.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Services.Diagnostics;
public static class PortalDiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamManagementPortalDiagnostics(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementApplicationAccess();
        services.AddOptions<PortalDiagnosticsOptions>().Bind(configuration.GetSection(PortalDiagnosticsOptions.SectionName))
            .Validate(x => x.IsValid(), "Bounded portal diagnostics settings and a 32-byte protection key are required.").ValidateOnStart();
        services.AddOptions<ManagementConnectionOptions>().Bind(configuration.GetSection(ManagementConnectionOptions.SectionName))
            .Validate(x => x.ApprovedOrigins is { Length: > 0 } && x.TimeoutSeconds is >= 1 and <= 30, "Approved management HTTPS origins are required.").ValidateOnStart();
        services.TryAddSingleton<IManagementTargetDnsResolver, ManagementTargetDnsResolver>();
        services.TryAddSingleton<IManagementTargetSocketConnector, ManagementTargetSocketConnector>();
        services.TryAddSingleton<ManagementConnectionNetworkPolicy>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PortalDiagnosticTokens>();
        services.TryAddScoped<IManagementDiagnosticConnectionResolver, ManagementDiagnosticConnectionResolver>();
        services.TryAddScoped<IApplicationDiagnosticsClient, ApplicationDiagnosticsClient>();
        services.TryAddScoped<IManagementDiagnosticsService, ManagementDiagnosticsService>();
        return services;
    }
}
