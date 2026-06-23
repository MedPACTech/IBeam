namespace IBeam.Ai;

public interface IAgentToolContextFactory
{
    AgentToolContext Create(IServiceProvider services);
}
