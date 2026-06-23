namespace IBeam.Ai;

internal sealed class AgentToolCatalog : IAgentToolCatalog
{
    private readonly Dictionary<string, AgentToolDefinition> _byName;

    public AgentToolCatalog(IEnumerable<AgentToolDefinition> tools)
    {
        Tools = tools.ToArray();
        _byName = new Dictionary<string, AgentToolDefinition>(StringComparer.Ordinal);

        foreach (var tool in Tools)
        {
            if (!_byName.TryAdd(tool.Name, tool))
                throw new InvalidOperationException($"Duplicate agent tool name '{tool.Name}'.");
        }
    }

    public IReadOnlyList<AgentToolDefinition> Tools { get; }

    public AgentToolDefinition? Find(string name)
        => _byName.TryGetValue(name, out var tool) ? tool : null;
}
