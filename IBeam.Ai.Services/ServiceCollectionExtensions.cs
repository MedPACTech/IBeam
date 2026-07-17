using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IBeam.Services.Abstractions;

namespace IBeam.Ai;

public static class AiServicesServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAiServices(
        this IServiceCollection services,
        Action<AgentToolRegistryBuilder>? configureTools = null)
    {
        services.AddIBeamServicePolicies();
        services.AddIBeamServiceAuditing();

        var builder = new AgentToolRegistryBuilder();
        configureTools?.Invoke(builder);

        services.TryAddScoped<IAgentToolAccessPolicy, DefaultAgentToolAccessPolicy>();
        services.TryAddScoped<IAgentMcpService, AgentMcpService>();
        services.AddSingleton<IAgentToolCatalog>(builder.Build());

        return services;
    }
}
