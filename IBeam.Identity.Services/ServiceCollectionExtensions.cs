using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Services.Otp;
using IBeam.Identity.Services.Auth;
using IBeam.Identity.Services.Tenants;
using IBeam.Identity.Services.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using IBeam.Identity.Abstractions.Options;

namespace IBeam.Identity.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IBeam Identity core services and options for DI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddIBeamIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {

        services.AddOptions<OtpOptions>()
        .Bind(configuration.GetSection(OtpOptions.SectionName))
        .Validate(o =>
        {
            o.Validate();
            return true;
        })
        .ValidateOnStart();

        services.AddOptions<JwtOptions>()
        .Bind(configuration.GetSection(JwtOptions.SectionName))
        .Validate(o =>
        {
            o.Validate();
            return true;
        })
        .ValidateOnStart();

        services.AddOptions<FeatureOptions>()
        .Bind(configuration.GetSection("IBeam:Identity:Features"));

        services.AddOptions<OAuthOptions>()
        .Bind(configuration.GetSection(OAuthOptions.SectionName));

        // Core services
        services.AddScoped<IOtpService, OtpService>();
        //services.AddScoped<IIdentityAuthService, IdentityAuthService>();
        services.AddScoped<ITenantSelectionService, TenantSelectionService>();
        services.AddScoped<ITokenService, JwtTokenService>();


        // Note: IOtpChallengeStore, ISender, and other dependencies must be registered by the consumer or by repository/communications packages.

        return services;
    }

    /// <summary>
    /// Registers IBeam Identity core auth services and options for DI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddIBeamIdentityAuthPasswordService(this IServiceCollection services)
    {
        // Core services
        services.AddScoped<IIdentityAuthService, PasswordAuthService>();
        return services;
    }

    /// <summary>
    /// Registers IBeam Identity core auth services and options for DI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddIBeamIdentityAuthOtpService(this IServiceCollection services)
    {
        // Core services
        services.AddScoped<IIdentityOtpAuthService, OtpAuthService>();
        return services;
    }

    public static IServiceCollection AddIBeamIdentityAuthOAuthService(this IServiceCollection services)
    {
        services.AddScoped<IIdentityOAuthAuthService, OAuthAuthService>();
        return services;
    }
}
