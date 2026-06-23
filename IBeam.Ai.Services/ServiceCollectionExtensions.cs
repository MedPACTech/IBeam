using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Ai;

public static class AiServicesServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAiServices(
        this IServiceCollection services,
        Action<AgentToolRegistryBuilder>? configureTools = null)
    {
        var builder = new AgentToolRegistryBuilder();
        configureTools?.Invoke(builder);

        services.TryAddScoped<IAgentToolAccessPolicy, DefaultAgentToolAccessPolicy>();
        services.TryAddScoped<IAgentMcpService, AgentMcpService>();
        services.AddSingleton<IAgentToolCatalog>(builder.Build());

        return services;
    }
}
