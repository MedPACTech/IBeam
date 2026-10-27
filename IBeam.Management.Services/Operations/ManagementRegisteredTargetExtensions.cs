using IBeam.Management.CustomerIdentity;
using IBeam.Management.Operations;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IBeam.Management.Services;

public static class ManagementRegisteredTargetExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementRegisteredOperationsTarget(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementOperationCapabilities(configuration); services.AddIBeamManagementAudit(configuration);
        services.AddOptions<ManagementOperationTargetOptions>().Validate(o => o.RealmId != Guid.Empty && o.DedicatedApplicationRealm &&
            o.TrustedPortals is { Count: > 0 and <= 100 } && o.TrustedPortals.All(x => x.OrganizationId != Guid.Empty && x.ApplicationId != Guid.Empty &&
                ManagementDelegationBinding.ValidName(x.Issuer) && ManagementDelegationBinding.ValidName(x.Audience)) &&
            o.TrustedPortals.Select(x => (x.OrganizationId, x.ApplicationId, x.Issuer)).Distinct().Count() == o.TrustedPortals.Count,
            "Dedicated target realm and explicit trusted portal organization/application/issuer/audience bindings are required.").ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System); services.TryAddScoped<ManagementRegisteredRequestState>();
        services.TryAddScoped<ManagementDelegatedActorState>(); services.TryAddScoped<IManagementDelegatedActorContext>(p => p.GetRequiredService<ManagementDelegatedActorState>());
        services.TryAddScoped<ManagementRegisteredTargetAuthority>();
        services.TryAddScoped<IManagementOperationAccessService>(p => p.GetRequiredService<ManagementRegisteredTargetAuthority>());
        services.TryAddScoped<IManagementRegisteredOperationInfoService, ManagementRegisteredOperationInfoService>();
        services.TryAddScoped<IManagementRegisteredOperationDispatcher, ManagementRegisteredOperationDispatcher>();
        services.AddHostedService<Validate>(); return services;
    }
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            foreach (var descriptor in scope.ServiceProvider.GetRequiredService<ManagementOperationRegistry>().Descriptors)
                _ = ManagementRegisteredRoute.Path(descriptor, Guid.NewGuid(), Guid.NewGuid());
            _ = scope.ServiceProvider.GetRequiredService<IManagementRegisteredOperationDispatcher>();
            _ = scope.ServiceProvider.GetRequiredService<IManagementActorValidationKeyProvider>();
            _ = scope.ServiceProvider.GetRequiredService<IManagementDelegationReplayStore>();
            _ = scope.ServiceProvider.GetRequiredService<IManagementAuditRepository>(); return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
