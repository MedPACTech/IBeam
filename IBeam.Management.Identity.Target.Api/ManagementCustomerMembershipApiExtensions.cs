using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Memberships;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;
public static class ManagementCustomerMembershipApiExtensions
{
    public static IServiceCollection AddIBeamManagementCustomerMembershipApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementIdentityOperationsApi(configuration); services.AddIBeamManagementCustomerMemberships(configuration);
        foreach (var operation in new[] { ManagementCustomerIdentityOperations.MembershipsManage, ManagementCustomerIdentityOperations.RolesAssign })
            services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(operation));
        services.AddHostedService<Validator>();
        services.AddControllers().ConfigureApplicationPartManager(x => { if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); }); return services;
    }
    private sealed record Capability(string Operation) : IManagementCustomerIdentityCapability { public Type RequiredServiceType => typeof(IManagementCustomerMembershipService); }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) { var type = typeof(ManagementCustomerMembershipController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    { public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope();
        var enforcement = scope.ServiceProvider.GetRequiredService<IBeam.Identity.Interfaces.IIdentityTenantAccessEnforcement>();
        if (enforcement.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(enforcement.Mode) || enforcement.Mode.Length > 100)
            throw new InvalidOperationException("Current customer request/refresh access enforcement is required.");
        _ = scope.ServiceProvider.GetRequiredService<IManagementCustomerMembershipService>(); return Task.CompletedTask; } public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
}
