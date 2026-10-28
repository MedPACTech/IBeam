using IBeam.Management.Operations.Target.Api;
using IBeam.Management.TenantData.Target.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Management.TenantData.Target.Api;

public static class ManagementTenantDataApiExtensions
{
    public static IServiceCollection AddIBeamManagementTenantPurgeTargetApi(this IServiceCollection services, IConfiguration configuration, Action<ManagementTenantPurgeOptions> configure)
    {
        var options = new ManagementTenantPurgeOptions(); configure(options);
        if (!options.Enabled) return services;
        services.AddIBeamManagementTenantPurgeTarget(o => { o.Enabled = true; o.PolicyKeys = options.PolicyKeys;
            o.AllowVolatileDevelopmentJobs = options.AllowVolatileDevelopmentJobs; o.PlanLifetimeSeconds = options.PlanLifetimeSeconds; });
        return services.AddIBeamManagementRegisteredOperationsTargetApi(configuration);
    }

    public static IServiceCollection AddIBeamManagementTenantDataTargetApi(this IServiceCollection services, IConfiguration configuration, Action<ManagementTenantExportOptions> configure)
    {
        var options = new ManagementTenantExportOptions(); configure(options);
        if (!options.Enabled) return services;
        services.AddIBeamManagementTenantExportTarget(o => { o.Enabled = true; o.PolicyKeys = options.PolicyKeys; o.AllowVolatileDevelopmentJobs = options.AllowVolatileDevelopmentJobs; });
        return services.AddIBeamManagementRegisteredOperationsTargetApi(configuration);
    }
}
