using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Api;

public static class ManagementPortalCustomerDirectoriesApiExtensions
{
    public static IServiceCollection AddIBeamManagementPortalCustomerDirectoriesApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementCustomerIdentityApi(configuration);
        services.AddIBeamManagementPortalCustomerDirectories(configuration);
        services.AddHostedService<DependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(manager => {
            if (!manager.FeatureProviders.OfType<DirectoryFeature>().Any()) manager.FeatureProviders.Add(new DirectoryFeature()); });
        return services;
    }
    private sealed class DirectoryFeature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var type = typeof(ManagementPortalCustomerDirectoriesController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type);
        }
    }
    private sealed class DependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementPortalCustomerDirectory<ManagementCustomerTenantDto>>();
            _ = scope.ServiceProvider.GetRequiredService<IManagementPortalCustomerDirectory<ManagementCustomerUserDto>>();
            _ = scope.ServiceProvider.GetRequiredService<IManagementPortalCustomerDirectory<ManagementCustomerInvitationDto>>();
            return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
