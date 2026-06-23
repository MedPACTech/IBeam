using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Ai;

public static class AiApiServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAiMcp(
        this IServiceCollection services,
        Action<AgentToolRegistryBuilder>? configureTools = null)
    {
        services.AddIBeamAiServices(configureTools);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IAgentToolContextFactory, HttpAgentToolContextFactory>();

        return services;
    }
}
