using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace IBeam.Identity.Services.Tenants;

public static class IdentityTenantOwnershipAdministrationExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamIdentityTenantOwnershipAdministration(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddIBeamIdentityTenantOwnershipProtection(configuration);
        services.AddOptions<IdentityTenantOwnershipTransferOptions>().Bind(configuration.GetSection(IdentityTenantOwnershipTransferOptions.SectionName))
            .Validate(x => x.IsValid(), "Explicit SingleOwner or MultipleOwners transfer policy and a bounded snapshot are required.").ValidateOnStart();
        if (!configuration.GetValue<bool>(IdentityTenantOwnershipTransferOptions.SectionName + ":Enabled")) return services;
        services.TryAddScoped<IIdentityTenantOwnershipAdministrationService, IdentityTenantOwnershipAdministrationService>();
        services.AddHostedService<Validator>(); return services;
    }
    private sealed class Validator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IIdentityTenantOwnershipAdministrationService>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
