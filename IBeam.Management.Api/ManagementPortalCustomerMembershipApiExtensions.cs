using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Api;
public static class ManagementPortalCustomerMembershipApiExtensions
{
    public static IServiceCollection AddIBeamManagementPortalCustomerMembershipApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementPortalIdentityOperationsApi(configuration); services.AddIBeamManagementPortalCustomerDirectoriesApi(configuration);
        services.AddIBeamManagementPortalTenantAdministration(configuration);
        services.TryAddScoped<IManagementPortalCustomerMembershipService, ManagementPortalCustomerMembershipService>();
        services.AddHostedService<Validator>();
        services.AddControllers().ConfigureApplicationPartManager(x => { if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); }); return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var type in new[] { typeof(ManagementPortalCustomerMembershipController), typeof(ManagementPortalMutationOperationsController) }.Select(x => x.GetTypeInfo()))
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    { public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementPortalCustomerMembershipService>(); return Task.CompletedTask; } public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
}
