using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authorization.Policy;

namespace IBeam.Ai;

public static class AiApiServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAiMcp(
        this IServiceCollection services,
        Action<AgentToolRegistryBuilder>? configureTools = null,
        Action<IBeamMcpOAuthOptions>? configureOAuth = null)
    {
        services.AddIBeamAiServices(configureTools);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IAgentToolContextFactory, HttpAgentToolContextFactory>();
        services.AddOptions<IBeamMcpOAuthOptions>();
        if (configureOAuth is not null)
            services.Configure(configureOAuth);
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, IBeamMcpAuthorizationResultHandler>());

        return services;
    }
}
