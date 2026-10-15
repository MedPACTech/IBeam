using IBeam.Management;
using IBeam.Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Diagnostics.Api;

public static class ManagementDiagnosticsApiServiceCollectionExtensions
{
    /// <summary>Registers protected controllers; the host owns authentication and endpoint mapping.</summary>
    public static IServiceCollection AddIBeamManagementDiagnosticsApi(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ManagementDiagnosticsApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = services.AddOptions<ManagementDiagnosticsApiOptions>()
            .Bind(configuration.GetSection(ManagementDiagnosticsApiOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthenticationScheme),
                "An explicit management authentication scheme is required.")
            .ValidateOnStart();
        if (configure is not null) options.Configure(configure);
        services.AddOptions<ManagementDiagnosticsOptions>()
            .Bind(configuration.GetSection(ManagementDiagnosticsOptions.SectionName))
            .ValidateOnStart();
        // No default scheme or customer authentication options are changed.
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>()
            .Configure<IOptions<ManagementDiagnosticsApiOptions>>((authorization, configured) =>
                authorization.AddPolicy(ManagementDiagnosticsApiPolicies.Access, policy =>
                    policy.AddAuthenticationSchemes(configured.Value.AuthenticationScheme).RequireAuthenticatedUser()));
        services.AddHostedService<ManagementAuthenticationSchemeValidator>();
        services.AddHttpContextAccessor();
        services.TryAddScoped<IManagementPrincipalProvider, HttpManagementPrincipalProvider>();
        services.AddIBeamManagementServices();
        services.AddScoped<ManagementAccessDeniedFilter>();
        services.AddControllers().AddApplicationPart(typeof(ManagementDiagnosticsInfoController).Assembly);
        return services;
    }
}
