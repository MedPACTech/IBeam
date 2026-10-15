using IBeam.Management;
using IBeam.Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Api;

public static class ManagementApiServiceCollectionExtensions
{
    /// <summary>Registers protected controllers; the host owns authentication and endpoint mapping.</summary>
    public static IServiceCollection AddIBeamManagementApi(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ManagementApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = services.AddOptions<ManagementApiOptions>()
            .Bind(configuration.GetSection(ManagementApiOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthenticationScheme),
                "An explicit management authentication scheme is required.")
            .ValidateOnStart();
        if (configure is not null) options.Configure(configure);
        // No default scheme or customer authentication options are changed.
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>()
            .Configure<IOptions<ManagementApiOptions>>((authorization, configured) =>
                authorization.AddPolicy(ManagementApiPolicies.Access, policy =>
                    policy.AddAuthenticationSchemes(configured.Value.AuthenticationScheme).RequireAuthenticatedUser()));
        services.AddHostedService<ManagementAuthenticationSchemeValidator>();
        services.AddHttpContextAccessor();
        services.TryAddScoped<IManagementPrincipalProvider, HttpManagementPrincipalProvider>();
        services.AddIBeamManagementServices();
        services.AddScoped<ManagementAccessDeniedFilter>();
        services.AddControllers().AddApplicationPart(typeof(ManagementIdentityController).Assembly);
        return services;
    }
}
