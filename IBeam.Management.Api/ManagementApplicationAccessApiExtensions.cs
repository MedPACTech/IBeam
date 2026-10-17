using System.Reflection;
using IBeam.Management.Services;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Api;

public static class ManagementApplicationAccessApiExtensions
{
    public static IServiceCollection AddIBeamManagementApplicationAccessApi(this IServiceCollection services)
    {
        services.AddIBeamManagementApplicationsApi();
        services.AddIBeamManagementApplicationAccess();
        services.AddHostedService<AccessDependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            if (!manager.FeatureProviders.OfType<AccessFeatureProvider>().Any()) manager.FeatureProviders.Add(new AccessFeatureProvider());
        });
        return services;
    }
    private sealed class AccessFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var type = typeof(ManagementApplicationAccessController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type);
        }
    }
    private sealed class AccessDependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementApplicationAccessService>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
