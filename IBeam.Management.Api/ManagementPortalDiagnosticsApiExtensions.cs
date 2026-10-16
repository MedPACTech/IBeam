using System.Reflection;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Api;
public static class ManagementPortalDiagnosticsApiExtensions
{
    public static IServiceCollection AddIBeamManagementPortalDiagnosticsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementApplicationsApi();
        services.AddIBeamManagementPortalDiagnostics(configuration);
        services.AddHostedService<PortalDependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            if (!manager.FeatureProviders.OfType<PortalFeatureProvider>().Any()) manager.FeatureProviders.Add(new PortalFeatureProvider());
        });
        return services;
    }
    private sealed class PortalFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { var type = typeof(ManagementPortalDiagnosticsController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); }
    }
    private sealed class PortalDependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementDiagnosticsService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
