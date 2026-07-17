using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Services.Abstractions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddIBeamServicePolicies(
            this IServiceCollection services,
            Action<ServicePolicyOptions>? configure = null)
        {
            if (configure is null)
                services.AddOptions<ServicePolicyOptions>();
            else
                services.Configure(configure);

            services.AddSingleton<IServiceOperationPolicyResolver, ServiceOperationPolicyResolver>();
            services.TryAddScoped<IServiceOperationExecutor, ServiceOperationExecutor>();
            return services;
        }

        public static IServiceCollection AddIBeamServiceAuditing(
            this IServiceCollection services,
            IConfiguration? configuration = null,
            Action<ServiceAuditOptions>? configure = null)
        {
            if (configuration is not null)
            {
                services.AddOptions<ServiceAuditOptions>()
                    .Bind(configuration.GetSection(ServiceAuditOptions.SectionName));
            }
            else
            {
                services.AddOptions<ServiceAuditOptions>();
            }

            if (configure is not null)
            {
                services.Configure(configure);
            }

            services.TryAddScoped<IAuditTrailSink, NoOpAuditTrailSink>();
            services.TryAddScoped<IAuditActorProvider, NoOpAuditActorProvider>();
            services.TryAddScoped<IAuditRequestContextProvider, NoOpAuditRequestContextProvider>();
            services.TryAddScoped<IServiceOperationPrincipalProvider, NoOpServiceOperationPrincipalProvider>();
            services.TryAddScoped<IServiceOperationExecutor, ServiceOperationExecutor>();

            return services;
        }
    }
}
