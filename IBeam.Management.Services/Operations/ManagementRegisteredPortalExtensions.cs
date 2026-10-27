using IBeam.Management.Operations;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IBeam.Management.Services;

public static class ManagementRegisteredPortalExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementRegisteredOperationsPortal(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementConnections(configuration); services.AddIBeamManagementApplicationAccess();
        services.Configure<ManagementConnectionOptions>(o => o.EnableRegisteredOperationsDiscovery = true);
        services.AddOptions<ManagementRegisteredOperationClientOptions>().Bind(configuration.GetSection(ManagementRegisteredOperationClientOptions.SectionName))
            .Validate(o => ManagementDelegationBinding.ValidName(o.Issuer) && o.LifetimeSeconds is >= 10 and <= 60 && Key(o.OperationIdKey) &&
                o.Targets is { Count: > 0 and <= 100 } && o.Targets.All(x => x.ApplicationId != Guid.Empty && ManagementDelegationBinding.ValidName(x.Audience)) &&
                o.Targets.Select(x => x.ApplicationId).Distinct().Count() == o.Targets.Count,
                "Explicit issuer/audience targets, 10–60 second proofs and a persistent 32-byte operation ID key are required.").ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System); services.TryAddSingleton<ManagementCredentialSecretCodec>();
        services.TryAddScoped<IManagementTargetProfileProbe, ManagementTargetConnectionProbe>();
        services.TryAddScoped<IManagementRegisteredOperationAvailabilityService, ManagementRegisteredOperationAvailabilityService>();
        services.TryAddScoped<IManagementOperationClient, ManagementRegisteredOperationClient>(); services.AddHostedService<Validate>(); return services;
    }
    private static bool Key(string? value) { try { return value is not null && Convert.FromBase64String(value).Length == 32; } catch { return false; } }
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementOperationClient>(); _ = scope.ServiceProvider.GetRequiredService<IManagementActorSigningKeyProvider>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
