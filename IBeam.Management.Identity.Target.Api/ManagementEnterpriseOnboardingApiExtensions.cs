using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Onboarding;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;

public static class ManagementEnterpriseOnboardingApiExtensions
{
    public static IServiceCollection AddIBeamManagementEnterpriseOnboardingApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementTenantAdministrationApi(configuration);
        services.AddIBeamManagementEnterpriseOnboarding(configuration);
        foreach (var operation in new[] { ManagementCustomerIdentityOperations.OnboardingCreate, ManagementCustomerIdentityOperations.OnboardingRetry,
            ManagementCustomerIdentityOperations.InvitesManage, ManagementCustomerIdentityOperations.RolesAssign })
            services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(operation));
        services.AddHostedService<Validator>();
        services.AddControllers().ConfigureApplicationPartManager(x => {
            if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); });
        return services;
    }
    private sealed record Capability(string Operation) : IManagementCustomerIdentityCapability { public Type RequiredServiceType => typeof(IManagementEnterpriseOnboardingCommandService); }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
        var type = typeof(ManagementEnterpriseOnboardingController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    { public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementEnterpriseOnboardingCommandService>(); return Task.CompletedTask; }
      public Task StopAsync(CancellationToken ct) => Task.CompletedTask; }
}
