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
        builder.AddTool(
            name: "agentusers.me",
            description: "Return the current authenticated IBeam agent user identity and safe access context.",
            inputSchema: AgentToolSchemas.EmptyObject(),
            handler: (context, _, _) => ValueTask.FromResult<object?>(BuildAgentUserMe(context)));
        configureTools?.Invoke(builder);

        services.TryAddScoped<IAgentToolAccessPolicy, DefaultAgentToolAccessPolicy>();
        services.TryAddScoped<IAgentMcpService, AgentMcpService>();
        services.AddSingleton<IAgentToolCatalog>(builder.Build());

        return services;
    }

    private static object BuildAgentUserMe(AgentToolContext context)
        => new
        {
            principalType = context.AgentUserId is null ? "api-credential" : "agent",
            tenantId = context.TenantId,
            agentUserId = context.AgentUserId,
            displayName = context.AgentUserName,
            agentType = context.AgentType,
            agentKey = context.AgentKey,
            roles = Values(context.User, System.Security.Claims.ClaimTypes.Role, "role", "roles"),
            scopes = Values(context.User, "scope", "scopes", "scp"),
            tools = Values(context.User, "tool", "tools")
        };

    private static IReadOnlyList<string> Values(System.Security.Claims.ClaimsPrincipal principal, params string[] claimTypes)
        => claimTypes
            .SelectMany(type => principal.FindAll(type))
            .SelectMany(claim => claim.Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
