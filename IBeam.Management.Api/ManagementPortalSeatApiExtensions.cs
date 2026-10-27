using System.Reflection;
using IBeam.Management.Licensing;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IBeam.Management.Api;

public static class ManagementPortalSeatApiExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementPortalSeatsApi(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementApi(configuration); services.TryAddScoped<ManagementApplicationApiFilter>();
        services.AddHostedService<Validate>();
        services.AddControllers().ConfigureApplicationPartManager(x => x.FeatureProviders.Add(new Feature())); return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { var type = typeof(ManagementPortalSeatController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); }
    }
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementSeatPortalService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
