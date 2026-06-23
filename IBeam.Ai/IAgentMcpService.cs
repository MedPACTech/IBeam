using System.Text.Json;

namespace IBeam.Ai;

public interface IAgentMcpService
{
    Task<McpHttpResult> HandleAsync(JsonElement payload, CancellationToken cancellationToken = default);
}
