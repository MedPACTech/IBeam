using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Api;

public static class ManagementPortalIdentityOperationsApiExtensions
{
    public static IServiceCollection AddIBeamManagementPortalIdentityOperationsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementCustomerIdentityApi(configuration);
        services.AddIBeamManagementPortalCustomerDirectories(configuration);
        services.AddHostedService<DependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(x => {
            if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); });
        return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
            var type = typeof(ManagementPortalIdentityOperationsController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); }
    }
    private sealed class DependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementPortalIdentityOperationService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
