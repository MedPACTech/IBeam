using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace IBeam.Identity.Services.Tenants;

public static class IdentityTenantOwnershipExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamIdentityTenantOwnershipProtection(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddOptions<IdentityTenantOwnershipOptions>().Bind(configuration.GetSection(IdentityTenantOwnershipOptions.SectionName))
            .Validate(x => x.IsValid(), "Ownership requires explicit distinct owner names and Enabled=true; no default owner is inferred.").ValidateOnStart();
        if (!configuration.GetValue<bool>(IdentityTenantOwnershipOptions.SectionName + ":Enabled")) return services;
        services.AddIBeamIdentityTenantMembershipAdministration();
        services.TryAddScoped<IIdentityTenantOwnershipProtection, IdentityTenantOwnershipProtection>();
        services.AddHostedService<Validator>(); return services;
    }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope();
            if (scope.ServiceProvider.GetRequiredService<IIdentityTenantOwnershipProtection>().ProtocolVersion != 1)
                throw new InvalidOperationException("Compatible native ownership protection is required."); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
