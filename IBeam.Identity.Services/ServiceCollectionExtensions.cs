using IBeam.Identity.Interfaces;
using IBeam.Identity.Events;
using IBeam.Identity.Services.Otp;
using IBeam.Identity.Services.Auth;
using IBeam.Identity.Services.Tenants;
using IBeam.Identity.Services.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IBeam.Identity.Options;

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

        services.AddOptions<IdentityEmailTemplateOptions>()
        .Bind(configuration.GetSection(IdentityEmailTemplateOptions.SectionName));

        services.AddIBeamAuthEvents(configuration);

        // Core services
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ITenantSelectionService, TenantSelectionService>();
        services.AddScoped<ITokenService, JwtTokenService>();


        // Note: IOtpChallengeStore, ISender, and other dependencies must be registered by the consumer or by repository/communications packages.

        return services;
    }

    public static IServiceCollection AddIBeamAuthEvents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuthEventOptions>()
            .Bind(configuration.GetSection(AuthEventOptions.SectionName));

        services.TryAddScoped<IAuthEventPublisher, NoOpAuthEventPublisher>();
        services.TryAddScoped<IAuthLifecycleHook, NoOpAuthLifecycleHook>();

        return services;
    }

    public static IServiceCollection AddIBeamAuthEvents(
        this IServiceCollection services,
        Action<AuthEventOptions> configure)
    {
        services.Configure(configure);
        services.TryAddScoped<IAuthEventPublisher, NoOpAuthEventPublisher>();
        services.TryAddScoped<IAuthLifecycleHook, NoOpAuthLifecycleHook>();
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
