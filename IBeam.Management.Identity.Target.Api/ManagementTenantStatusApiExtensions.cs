using System.Reflection;
using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Tenants;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;
public static class ManagementTenantStatusApiExtensions
{
    public static IServiceCollection AddIBeamManagementTenantStatusApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementIdentityOperationsApi(configuration); services.AddIBeamManagementTenantStatus(configuration);
        services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability()); services.AddHostedService<Validator>();
        services.AddControllers().ConfigureApplicationPartManager(x => { if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); }); return services;
    }
    private sealed class Capability : IManagementCustomerIdentityCapability
    { public string Operation => ManagementCustomerIdentityOperations.TenantsStatus; public Type RequiredServiceType => typeof(IManagementTenantStatusService); }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) { var type = typeof(ManagementTenantStatusController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    { public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); var p = scope.ServiceProvider;
        var enforcement = p.GetRequiredService<IIdentityTenantAccessEnforcement>();
        if (enforcement.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(enforcement.Mode) ||
            p.GetRequiredService<IIdentityTenantAdministrationService>() is not IIdentityTenantStatusAdministrationService { SupportsConditionalStatus: true })
            throw new InvalidOperationException("Conditional tenant status and customer request/refresh enforcement are required.");
        _ = p.GetRequiredService<IManagementTenantStatusService>(); return Task.CompletedTask; } public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
}
