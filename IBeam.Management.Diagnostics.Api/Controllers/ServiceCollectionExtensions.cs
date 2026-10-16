using System.Reflection;
using IBeam.Management.Services.Diagnostics;
using IBeam.Management.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Diagnostics.Api.Controllers;

public static class ManagementSystemErrorApiServiceCollectionExtensions
{
    /// <summary>Optional read-only error routes. Host supplies target authentication and the isolated error repository.</summary>
    public static IServiceCollection AddIBeamManagementSystemErrorsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementSystemErrorService(configuration);
        services.TryAddScoped<SystemErrorApiFilter>();
        services.AddHostedService<ErrorApiDependencyValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IManagementDiagnosticsCapability, SystemErrorCapability>());
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            if (!manager.FeatureProviders.OfType<ErrorControllerFeatureProvider>().Any())
                manager.FeatureProviders.Add(new ErrorControllerFeatureProvider());
        });
        return services;
    }

    private sealed class ErrorApiDependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<ISystemErrorReaderService>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SystemErrorCapability : IManagementDiagnosticsCapability
    {
        public string Operation => ManagementOperations.ErrorsRead;
    }
    private sealed class ErrorControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var controller = typeof(SystemErrorsController).GetTypeInfo();
            if (!feature.Controllers.Contains(controller)) feature.Controllers.Add(controller);
        }
    }
}
