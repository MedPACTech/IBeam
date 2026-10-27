using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;

public static class ManagementIdentityOperationsApiExtensions
{
    public static IServiceCollection AddIBeamManagementIdentityOperationsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementIdentityOperations(configuration);
        services.AddSingleton<IManagementIdentityReceiptProtocol, ReceiptProtocol>();
        services.AddSingleton<IManagementCustomerIdentityCapability, Capability>();
        services.AddHostedService<DependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(x => {
            if (!x.FeatureProviders.OfType<Feature>().Any()) x.FeatureProviders.Add(new Feature()); });
        return services;
    }
    private sealed class Capability : IManagementCustomerIdentityCapability
    {
        public string Operation => ManagementCustomerIdentityOperations.OnboardingRead;
        public Type RequiredServiceType => typeof(IManagementIdentityOperationService);
    }
    private sealed class ReceiptProtocol : IManagementIdentityReceiptProtocol { public int Version => 1; }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
            foreach (var type in new[] { typeof(ManagementIdentityOperationsController), typeof(ManagementIdentityOperationLookupController) }.Select(x => x.GetTypeInfo()))
                if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); }
    }
    private sealed class DependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementIdentityOperationService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
