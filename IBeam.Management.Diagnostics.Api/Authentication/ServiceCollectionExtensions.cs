using System.Net;
using IBeam.Management.Services.Credentials;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Diagnostics.Api.Authentication;

public static class ManagementCredentialAuthenticationServiceCollectionExtensions
{
    /// <summary>Opt-in persisted-key authentication; leaves customer schemes/defaults unchanged. Supply the repository separately.</summary>
    public static IServiceCollection AddIBeamManagementDiagnosticsCredentialApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IAuditActorProvider, ManagementCredentialAuditContext>();
        services.TryAddScoped<IAuditRequestContextProvider, ManagementCredentialAuditContext>();
        services.AddIBeamManagementCredentialValidation(configuration);
        services.AddOptions<ManagementCredentialNetworkOptions>()
            .Bind(configuration.GetSection(ManagementCredentialNetworkOptions.SectionName))
            .Validate(o => o.AllowedNetworks is { Count: <= 100 } &&
                o.AllowedNetworks.All(c => IPNetwork.TryParse(c, out _)), "AllowedNetworks must contain at most 100 valid CIDR ranges.")
            .ValidateOnStart();
        services.TryAddSingleton<ManagementCredentialNetworkPolicy>();
        services.TryAddScoped<IManagementCredentialAuthenticationService, ManagementCredentialAuthenticationService>();
        services.TryAddScoped<IManagementDiagnosticsAccessService, ManagementDiagnosticsAccessService>();
        services.AddAuthentication().AddScheme<ManagementCredentialAuthenticationOptions, ManagementCredentialAuthenticationHandler>(
            ManagementCredentialAuthenticationDefaults.Scheme, _ => { });
        services.AddIBeamManagementDiagnosticsApi(configuration,
            o => o.AuthenticationScheme = ManagementCredentialAuthenticationDefaults.Scheme);
        return services;
    }
}
