using System.Text.Json;

namespace IBeam.Ai;

public delegate ValueTask<object?> AgentToolHandler(
    AgentToolContext context,
    JsonElement arguments,
    CancellationToken cancellationToken);

public sealed class AgentToolDefinition
{
    public AgentToolDefinition(
        string name,
        string description,
        object inputSchema,
        AgentToolHandler handler,
        string? moduleKey = null,
        IEnumerable<string>? requiredScopes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tool name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Tool description is required.", nameof(description));

        Name = name.Trim();
        Description = description.Trim();
        InputSchema = inputSchema ?? AgentToolSchemas.EmptyObject();
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        ModuleKey = string.IsNullOrWhiteSpace(moduleKey) ? null : moduleKey.Trim();
        RequiredScopes = (requiredScopes ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string Name { get; }
    public string Description { get; }
    public object InputSchema { get; }
    public AgentToolHandler Handler { get; }
    public string? ModuleKey { get; }
    public IReadOnlyList<string> RequiredScopes { get; }
}
