using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Ownership;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;

public static class ManagementCustomerOwnershipApiExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementCustomerOwnershipApi(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddIBeamManagementIdentityOperationsApi(configuration); services.AddIBeamManagementCustomerOwnership(configuration);
        foreach (var op in new[] { ManagementCustomerIdentityOperations.OwnersRead, ManagementCustomerIdentityOperations.OwnersTransfer })
            services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(op));
        services.AddHostedService<Validator>();
        services.AddControllers().ConfigureApplicationPartManager(x => x.FeatureProviders.Add(new Feature())); return services;
    }
    private sealed record Capability(string Operation) : IManagementCustomerIdentityCapability { public Type RequiredServiceType => typeof(IManagementCustomerOwnershipService); }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) { var type = typeof(ManagementCustomerOwnershipController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope();
            var enforcement = scope.ServiceProvider.GetRequiredService<IBeam.Identity.Interfaces.IIdentityTenantAccessEnforcement>();
            if (enforcement.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(enforcement.Mode) || enforcement.Mode.Length > 100 ||
                scope.ServiceProvider.GetRequiredService<IBeam.Identity.Interfaces.IIdentityTenantMembershipConditionalStore>() is not
                    IBeam.Identity.Interfaces.IIdentityTenantOwnershipTransferStore { OwnershipProtocolVersion: 1, RequiresOwnershipProtection: true })
                throw new InvalidOperationException("Atomic native ownership storage and customer request/refresh enforcement are required.");
            _ = scope.ServiceProvider.GetRequiredService<IBeam.Identity.Interfaces.IIdentityTenantOwnershipAdministrationService>();
            if (scope.ServiceProvider.GetRequiredService<IManagementCustomerOwnershipService>().ProtocolVersion != 1)
                throw new InvalidOperationException("Compatible atomic ownership administration is required."); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
