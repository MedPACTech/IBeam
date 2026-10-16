using IBeam.Management.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Services.Diagnostics;

public static class ManagementSystemErrorReaderServiceCollectionExtensions
{
    /// <summary>Internal reader only; no HTTP endpoints or diagnostic capability advertisement are added.</summary>
    public static IServiceCollection AddIBeamManagementSystemErrorReader(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementServices();
        services.AddOptions<ManagementDiagnosticsOptions>().Bind(configuration.GetSection(ManagementDiagnosticsOptions.SectionName)).ValidateOnStart();
        services.AddOptions<SystemErrorReaderOptions>().Bind(configuration.GetSection(SystemErrorReaderOptions.SectionName))
            .Validate(o => o.IsValid(), "Explicit diagnostic source isolation, bounded options and a 32-byte protection key are required.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DiagnosticTokenCodec>();
        // Host supplies both the read repository and a live IManagementDiagnosticsAccessService.
        services.TryAddScoped<IManagementSystemErrorReader, ManagementSystemErrorReader>();
        return services;
    }
}
