using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Tenants;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;

public static class ManagementTenantAdministrationApiExtensions
{
    public static IServiceCollection AddIBeamManagementTenantAdministrationApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementIdentityOperationsApi(configuration);
        services.AddIBeamManagementTenantAdministration(configuration);
        services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(ManagementCustomerIdentityOperations.TenantsCreate, typeof(IManagementTenantAdministrationService)));
        services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(ManagementCustomerIdentityOperations.TenantsUpdate, typeof(IManagementTenantRenameCapability)));
        services.AddHostedService<DependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(x => {
            if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); });
        return services;
    }
    private sealed record Capability(string Operation, Type RequiredServiceType) : IManagementCustomerIdentityCapability;
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
            var type = typeof(ManagementTenantAdministrationController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); }
    }
    private sealed class DependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementTenantAdministrationService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
