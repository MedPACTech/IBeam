using IBeam.Api.Models;

namespace IBeam.Api.Infrastructure;

public static class ConfigurationDependencyInjection
{
    public static IServiceCollection AddIBeamApiConfigurations(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ApiConfigurationBuilder>? configure = null)
    {
        services.Configure<ApiErrorHandlingOptions>(configuration.GetSection("ApiErrorHandling"));

        if (configure is null)
        {
            return services;
        }

        var builder = new ApiConfigurationBuilder(services, configuration);
        configure(builder);
        return services;
    }
}
