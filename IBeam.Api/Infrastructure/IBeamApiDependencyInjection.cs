using IBeam.Api.Models;

namespace IBeam.Api.Infrastructure;

public static class IBeamApiDependencyInjection
{
    public static IServiceCollection AddIBeamApi(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBeamApiBuilder>? configure = null)
    {
        services.AddHttpContextAccessor();
        services.AddControllers();
        services.Configure<ApiErrorHandlingOptions>(configuration.GetSection("ApiErrorHandling"));
        services.AddHostedService<GlobalErrorHandler>();

        if (configure is null)
        {
            return services;
        }

        var builder = new IBeamApiBuilder(services, configuration);
        configure(builder);
        return services;
    }
}
