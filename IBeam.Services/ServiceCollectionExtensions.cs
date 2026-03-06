using Microsoft.Extensions.DependencyInjection;

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
            return services;
        }
    }
}
