using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Identity.Services.Invites;

public static class TenantInviteLinkServiceCollectionExtensions
{
    /// <summary>
    /// Configures where invite links may point (IBM-0068). Called by <c>AddIBeamIdentityServices</c> with
    /// configuration only; call it again from an app to add origins or rules in code.
    /// </summary>
    /// <param name="configuration">
    /// Bound from <c>IBeam:Identity:Invites:Links</c>. Optional — an app can configure entirely in code.
    /// </param>
    /// <param name="configure">
    /// Runs after configuration binding, so it sees the configured values and can add to them. This is the
    /// point of the overload: an app can build the allowlist from its own CORS origins or per-environment
    /// settings rather than duplicating them in a config file.
    /// </param>
    public static IServiceCollection AddIBeamTenantInviteLinks(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<TenantInviteLinkOptions>? configure = null)
    {
        var builder = services.AddOptions<TenantInviteLinkOptions>();

        if (configuration is not null)
        {
            // Replacing rather than appending, so an app that configures AllowedOrigins gets exactly those
            // origins. IConfiguration.Bind adds to a collection, which is what made IBM-0072 and IBM-0073
            // possible; an allowlist is the last place to repeat that mistake.
            builder.Configure(options => ConfigurationCollectionBinder.BindReplacingSpecifiedCollections(
                configuration.GetSection(TenantInviteLinkOptions.SectionName),
                options));
        }

        if (configure is not null)
            builder.Configure(configure);

        services.TryAddScoped<ITenantInviteLinkPolicy, DefaultTenantInviteLinkPolicy>();
        return services;
    }

    /// <summary>Configures invite links entirely in code, for an app that keeps no invite settings in configuration.</summary>
    public static IServiceCollection AddIBeamTenantInviteLinks(
        this IServiceCollection services,
        Action<TenantInviteLinkOptions> configure)
        => services.AddIBeamTenantInviteLinks(configuration: null, configure: configure);
}
