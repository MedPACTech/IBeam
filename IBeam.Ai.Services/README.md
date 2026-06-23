# IBeam.Ai.Services

MCP protocol orchestration, tool registration, and default role/scope filtering for IBeam-backed applications.

## Setup

Register services and tools:

```csharp
builder.Services.AddIBeamAiServices(tools =>
{
    tools.AddTool(
        name: "app.work.list_cards",
        description: "List work cards visible to the current agent.",
        inputSchema: AgentToolSchemas.EmptyObject(),
        requiredScopes: ["api-scope:work"],
        handler: async (context, arguments, ct) =>
        {
            var service = context.Services.GetRequiredService<IWorkItemService>();
            return await service.ListAsync(ct);
        });
});
```

For ASP.NET Core MCP endpoint wiring, use `IBeam.Ai.Api`.

## Extensibility

Replace `IAgentToolAccessPolicy` when module access needs app-specific rules, such as checking an app-owned access-control service.
