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

public static class ManagementSystemLogApiServiceCollectionExtensions
{
    /// <summary>Optional read-only log routes. Host supplies target authentication and the isolated log repository.</summary>
    public static IServiceCollection AddIBeamManagementSystemLogsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementSystemLogService(configuration);
        services.TryAddScoped<ManagementDiagnosticApiFilter>();
        services.AddHostedService<LogApiDependencyValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IManagementDiagnosticsCapability, SystemLogCapability>());
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            if (!manager.FeatureProviders.OfType<LogControllerFeatureProvider>().Any())
                manager.FeatureProviders.Add(new LogControllerFeatureProvider());
        });
        return services;
    }

    private sealed class LogApiDependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<ISystemLogReaderService>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SystemLogCapability : IManagementDiagnosticsCapability
    {
        public string Operation => ManagementOperations.LogsRead;
    }
    private sealed class LogControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var controller = typeof(SystemLogsController).GetTypeInfo();
            if (!feature.Controllers.Contains(controller)) feature.Controllers.Add(controller);
        }
    }
}
