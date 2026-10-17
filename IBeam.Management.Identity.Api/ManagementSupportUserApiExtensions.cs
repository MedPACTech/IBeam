using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Api;
public static class ManagementSupportUserApiExtensions
{
    /// <summary>Optional routes on the isolated Management Identity host; generic Identity controllers stay absent.</summary>
    public static IServiceCollection AddIBeamManagementSupportUsersApi(this IServiceCollection services)
    {
        services.AddScoped<ManagementSupportApiFilter>();
        services.AddHostedService<SupportDependencyValidator>();
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            if (!manager.FeatureProviders.OfType<SupportFeatureProvider>().Any()) manager.FeatureProviders.Add(new SupportFeatureProvider());
        });
        return services;
    }
    private sealed class SupportFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var type = typeof(ManagementSupportUsersController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type);
        }
    }
    private sealed class SupportDependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementSupportUserService>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
