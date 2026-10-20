using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Api;
public static class ManagementPortalEnterpriseOnboardingApiExtensions
{
    public static IServiceCollection AddIBeamManagementPortalEnterpriseOnboardingApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementPortalTenantAdministrationApi(configuration);
        services.AddIBeamManagementPortalEnterpriseOnboarding(configuration);
        services.AddHostedService<Validator>();
        services.AddControllers().ConfigureApplicationPartManager(x => {
            if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); }); return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        var type = typeof(ManagementPortalEnterpriseOnboardingController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    { public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementPortalEnterpriseOnboardingService>(); return Task.CompletedTask; }
      public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
}
