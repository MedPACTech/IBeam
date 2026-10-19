using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Tenants;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;

public static class ManagementTenantDirectoryApiExtensions
{
    /// <summary>Requires target API/delegation and a separately configured read repository. Does not create tables.</summary>
    public static IServiceCollection AddIBeamManagementTenantDirectoryApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementTenantDirectory(configuration);
        services.AddSingleton<IManagementCustomerIdentityCapability, Capability>();
        services.AddHostedService<DependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(x =>
        {
            if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature());
        });
        return services;
    }
    private sealed class Capability : IManagementCustomerIdentityCapability
    {
        public string Operation => ManagementCustomerIdentityOperations.TenantsRead;
        public Type RequiredServiceType => typeof(IManagementTenantDirectoryQuery);
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var controller = typeof(ManagementTenantDirectoryController).GetTypeInfo();
            if (!feature.Controllers.Contains(controller)) feature.Controllers.Add(controller);
        }
    }
    private sealed class DependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementTenantDirectoryService>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
