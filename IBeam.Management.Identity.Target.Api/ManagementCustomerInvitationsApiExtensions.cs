using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Invitations;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;

public static class ManagementCustomerInvitationsApiExtensions
{
    public static IServiceCollection AddIBeamManagementCustomerInvitationsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementIdentityOperationsApi(configuration);
        services.AddIBeamManagementCustomerInvitationDirectoryApi(configuration);
        services.AddIBeamManagementCustomerInvitations(configuration);
        services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability());
        services.AddControllers().ConfigureApplicationPartManager(x => { if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); });
        services.AddHostedService<Validator>(); return services;
    }
    private sealed class Capability : IManagementCustomerIdentityCapability
    { public string Operation => ManagementCustomerIdentityOperations.InvitesManage; public Type RequiredServiceType => typeof(IManagementCustomerInvitationService); }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) { var type = typeof(ManagementCustomerInvitationsController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    { public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementCustomerInvitationService>(); return Task.CompletedTask; } public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
}
