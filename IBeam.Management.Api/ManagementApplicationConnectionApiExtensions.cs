using System.Reflection;
using IBeam.Management.Services;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Api;

public static class ManagementApplicationConnectionApiExtensions
{
    public static IServiceCollection AddIBeamManagementConnectionsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementApplicationsApi();
        services.AddIBeamManagementConnections(configuration);
        services.AddHostedService<ConnectionDependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            if (!manager.FeatureProviders.OfType<ConnectionFeatureProvider>().Any()) manager.FeatureProviders.Add(new ConnectionFeatureProvider());
        });
        return services;
    }
    private sealed class ConnectionFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var type = typeof(ManagementApplicationConnectionsController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type);
        }
    }
    private sealed class ConnectionDependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementApplicationConnectionService>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
