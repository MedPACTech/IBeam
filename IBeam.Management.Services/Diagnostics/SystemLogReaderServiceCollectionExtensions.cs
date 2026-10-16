using IBeam.Management.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Services.Diagnostics;

public static class ManagementSystemLogReaderServiceCollectionExtensions
{
    /// <summary>Internal reader only; no HTTP endpoints or diagnostic capability advertisement are added.</summary>
    public static IServiceCollection AddIBeamManagementSystemLogReader(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementServices();
        services.AddOptions<ManagementDiagnosticsOptions>().Bind(configuration.GetSection(ManagementDiagnosticsOptions.SectionName)).ValidateOnStart();
        services.AddOptions<SystemLogReaderOptions>().Bind(configuration.GetSection(SystemLogReaderOptions.SectionName))
            .Validate(o => o.IsValid(), "Explicit diagnostic source isolation, bounded options, approved sources and a 32-byte protection key are required.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SystemLogTokenCodec>();
        // Host supplies both the read repository and a live IManagementDiagnosticsAccessService.
        services.TryAddScoped<IManagementSystemLogReader, ManagementSystemLogReader>();
        return services;
    }

    public static IServiceCollection AddIBeamManagementSystemLogService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementSystemLogReader(configuration);
        services.TryAddSingleton<SystemLogProjection>();
        services.TryAddScoped<ISystemLogReaderService, SystemLogReaderService>();
        return services;
    }
}
