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

public static class ManagementPortalCustomerOwnershipApiExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementPortalCustomerOwnershipApi(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddIBeamManagementPortalIdentityOperationsApi(configuration); services.AddIBeamManagementPortalCustomerDirectoriesApi(configuration);
        services.AddIBeamManagementPortalTenantAdministration(configuration);
        services.TryAddScoped<IManagementPortalCustomerOwnershipService, ManagementPortalCustomerOwnershipService>();
        services.AddHostedService<Validator>();
        services.AddControllers().ConfigureApplicationPartManager(x => x.FeatureProviders.Add(new Feature())); return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        foreach (var type in new[] { typeof(ManagementPortalCustomerOwnershipController), typeof(ManagementPortalMutationOperationsController) }.Select(x => x.GetTypeInfo()))
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    { public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementPortalCustomerOwnershipService>(); return Task.CompletedTask; }
      public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
}
