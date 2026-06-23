# IBeam.Ai.Api

ASP.NET Core endpoint wiring for IBeam AI agent and MCP tooling.

## Setup

Register tools:

```csharp
builder.Services.AddIBeamAiMcp(tools =>
{
    tools.AddTool(
        name: "hubbsly.work.list_cards",
        description: "List work cards visible to the current agent.",
        inputSchema: AgentToolSchemas.Object(
            new Dictionary<string, object>
            {
                ["limit"] = AgentToolSchemas.Integer("Maximum cards to return.")
            },
            []),
        moduleKey: "work",
        requiredScopes: ["api-scope:work"],
        handler: async (context, arguments, ct) =>
        {
            var service = context.Services.GetRequiredService<IWorkItemService>();
            return await service.ListAsync(ct);
        });
});
```

Map the MCP endpoint:

```csharp
app.MapIBeamMcp("/api/mcp");
```

The route requires authorization. With IBeam Identity API credentials, agents should call it with:

```http
X-API-Key: {raw-api-key}
```

or:

```http
Authorization: ApiKey {raw-api-key}
```

Do not send API credentials as Bearer tokens.
