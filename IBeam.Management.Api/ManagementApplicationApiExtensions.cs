using System.Reflection;
using IBeam.Management.Services;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Api;

public static class ManagementApplicationApiExtensions
{
    /// <summary>Requires AddIBeamManagementApi and an isolated application repository. No routes are added without this opt-in.</summary>
    public static IServiceCollection AddIBeamManagementApplicationsApi(this IServiceCollection services)
    {
        services.AddIBeamManagementApplications();
        services.TryAddScoped<ManagementApplicationApiFilter>();
        services.AddHostedService<ApplicationDependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            if (!manager.FeatureProviders.OfType<ApplicationsFeatureProvider>().Any()) manager.FeatureProviders.Add(new ApplicationsFeatureProvider());
        });
        return services;
    }
    private sealed class ApplicationsFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var type = typeof(ManagementApplicationsController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type);
        }
    }
    private sealed class ApplicationDependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementApplicationService>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
