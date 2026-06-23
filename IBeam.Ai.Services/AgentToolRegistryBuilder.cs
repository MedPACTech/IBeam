namespace IBeam.Ai;

public sealed class AgentToolRegistryBuilder
{
    private readonly List<AgentToolDefinition> _tools = [];

    public AgentToolRegistryBuilder AddTool(
        string name,
        string description,
        object inputSchema,
        AgentToolHandler handler,
        string? moduleKey = null,
        IEnumerable<string>? requiredScopes = null)
    {
        _tools.Add(new AgentToolDefinition(
            name,
            description,
            inputSchema,
            handler,
            moduleKey,
            requiredScopes));

        return this;
    }

    public AgentToolRegistryBuilder AddTool(AgentToolDefinition definition)
    {
        _tools.Add(definition ?? throw new ArgumentNullException(nameof(definition)));
        return this;
    }

    internal AgentToolCatalog Build()
        => new(_tools);
}
