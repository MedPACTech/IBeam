namespace IBeam.Ai;

public interface IAgentToolCatalog
{
    IReadOnlyList<AgentToolDefinition> Tools { get; }
    AgentToolDefinition? Find(string name);
}
