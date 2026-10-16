using IBeam.Management.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Management.Api;
public static class ManagementApplicationSelectorApiExtensions
{
    public static IServiceCollection AddIBeamManagementApplicationSelectorApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementPortalDiagnosticsApi(configuration);
        services.AddIBeamManagementApplicationSelector(configuration);
        return services;
    }
}
