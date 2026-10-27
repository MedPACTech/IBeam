using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Services.Delegation;

public static class ManagementDelegatedActorTargetExtensions
{
    private sealed class Registration;
    /// <summary>Host supplies pinned verification keys, an atomic replay store and a durable metadata audit repository.</summary>
    public static IServiceCollection AddIBeamManagementDelegatedActors(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddIBeamManagementCustomerIdentityTargetServices(configuration);
        services.AddIBeamManagementAudit(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ManagementActorTrustOptions>().Bind(configuration.GetSection(ManagementActorTrustOptions.SectionName))
            .Validate(x => x.TrustedPortals is { Count: > 0 and <= 100 } &&
                x.TrustedPortals.All(t => t is not null && t.OrganizationId != Guid.Empty && t.ApplicationId != Guid.Empty &&
                    ManagementDelegationBinding.ValidName(t.Issuer) && ManagementDelegationBinding.ValidName(t.Audience)) &&
                x.TrustedPortals.Select(t => (t.OrganizationId, t.ApplicationId, t.Issuer)).Distinct().Count() == x.TrustedPortals.Count,
                "Explicit trusted issuer/audience and support organization/application bindings are required.").ValidateOnStart();
        services.TryAddScoped<ManagementDelegatedActorContext>();
        services.TryAddScoped<IManagementDelegatedActorContext>(p => p.GetRequiredService<ManagementDelegatedActorContext>());
        services.TryAddScoped<ManagementCustomerIdentityCredentialGuard>();
        services.TryAddScoped<IManagementDelegatedActorValidator, ManagementDelegatedActorValidator>();
        services.TryAddScoped<IManagementCustomerIdentityTargetAccessService, ManagementCustomerIdentityTargetAccessService>();
        services.AddHostedService<DependencyValidator>();
        return services;
    }
    private sealed class DependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementDelegatedActorValidator>();
            _ = scope.ServiceProvider.GetRequiredService<IManagementAuditRepository>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
