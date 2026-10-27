using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Provisioning;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;
public static class ManagementCustomerProvisioningApiExtensions
{
    public static IServiceCollection AddIBeamManagementAssignableRolesApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementAssignableRoleCatalog(configuration);
        services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(ManagementCustomerIdentityOperations.RolesRead, typeof(IManagementAssignableRoleCatalogService)));
        services.AddControllers().ConfigureApplicationPartManager(x => { if (!x.FeatureProviders.OfType<RolesFeature>().Any()) x.FeatureProviders.Add(new RolesFeature()); });
        services.AddHostedService<RolesValidator>(); return services;
    }
    public static IServiceCollection AddIBeamManagementCustomerProvisioningApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementIdentityOperationsApi(configuration);
        services.AddIBeamManagementAssignableRolesApi(configuration);
        services.AddIBeamManagementCustomerProvisioning(configuration);
        foreach (var operation in ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.UsersProvision))
            services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(operation, typeof(IManagementCustomerProvisioningService)));
        services.AddControllers().ConfigureApplicationPartManager(x => { if (!x.FeatureProviders.OfType<ProvisionFeature>().Any()) x.FeatureProviders.Add(new ProvisionFeature()); });
        services.AddHostedService<ProvisionValidator>(); return services;
    }
    private sealed record Capability(string Operation, Type RequiredServiceType) : IManagementCustomerIdentityCapability;
    private sealed class RolesFeature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) { var type = typeof(ManagementAssignableRolesController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class ProvisionFeature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) { var type = typeof(ManagementCustomerProvisioningController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class RolesValidator(IServiceScopeFactory scopes) : IHostedService
    { public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementAssignableRoleCatalogService>(); return Task.CompletedTask; } public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
    private sealed class ProvisionValidator(IServiceScopeFactory scopes) : IHostedService
    { public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementCustomerProvisioningService>(); return Task.CompletedTask; } public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
}
