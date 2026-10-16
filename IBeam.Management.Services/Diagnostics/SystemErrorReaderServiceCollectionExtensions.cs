using IBeam.Management.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Services.Diagnostics;

public static class SafeSystemErrorReaderServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamManagementSystemErrorService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementSystemErrorReader(configuration);
        services.TryAddSingleton<SystemErrorProjection>();
        services.TryAddScoped<ISystemErrorReaderService, SystemErrorReaderService>();
        return services;
    }
}
