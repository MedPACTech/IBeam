using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Services.Logging;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamLoggerAuditTrail(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<ServiceAuditOptions>? configure = null)
    {
        services.AddIBeamServiceAuditing(configuration, configure);
        services.Replace(ServiceDescriptor.Scoped<IAuditTrailSink, LoggerAuditTrailSink>());
        return services;
    }

    public static IServiceCollection AddIBeamRepositoryAuditTrail(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<ServiceAuditOptions>? configure = null)
    {
        services.AddIBeamServiceAuditing(configuration, configure);
        services.Replace(ServiceDescriptor.Scoped<IAuditTrailSink, RepositoryAuditTrailSink>());
        return services;
    }

    public static IServiceCollection AddIBeamHttpContextAuditActor(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<IAuditActorProvider, HttpContextAuditActorProvider>());
        return services;
    }
}
