# IBeam.Ai.Api

`IBeam.Ai.Api` provides ASP.NET Core endpoint wiring for IBeam AI agent and MCP tooling.

```powershell
dotnet add package IBeam.Ai.Api
```

## When To Use This

- You want to expose an MCP endpoint from an ASP.NET Core API.
- You want API key-authenticated agents to call IBeam service-backed tools.
- You want tool context populated from the current HTTP user/principal.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Endpoint mapping | `MapIBeamMcp(...)` | Maps a POST endpoint for MCP JSON-RPC requests. |
| DI | `AddIBeamAiMcp(...)` | Registers AI services and HTTP context support. |
| Context factory | `HttpAgentToolContextFactory` | Resolves user, agent key, tenant ID, and API credential ID from claims. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

The MCP endpoint is an API gateway. It should authenticate the caller, delegate protocol handling to `IAgentMcpService`, and let tool handlers call service-layer methods.

## Quick Start

```csharp
using IBeam.Ai;

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

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
        requiredScopes: [ "api-scope:work" ],
        handler: async (context, arguments, ct) =>
        {
            var service = context.Services.GetRequiredService<IWorkItemService>();
            return await service.ListAsync(ct);
        });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapIBeamMcp("/api/mcp", authorizationPolicy: "AgentApi");
```

With IBeam Identity API credentials, agents should call the endpoint with:

```http
X-API-Key: {raw-api-key}
```

or:

```http
Authorization: ApiKey {raw-api-key}
```

Do not send API credentials as Bearer tokens unless the consuming app explicitly implements that behavior.

## Context Claims

`HttpAgentToolContextFactory` resolves these common fields from the authenticated principal:

| Context Field | Source |
|---|---|
| `User` | Current `HttpContext.User`. |
| `AgentKey` | Agent key claims such as IBeam agent key, alternate agent key, `agent`, `apiAgentKey`, or `apiAgentId`. |
| `TenantId` | IBeam tenant claim or alternate tenant claim. |
| `ApiCredentialId` | IBeam API credential ID claim. |

## Service Operations, Auditing, And Permissions

MCP access has two layers:

| Layer | Purpose |
|---|---|
| Endpoint authorization | Ensures only authenticated/allowed agents reach `/api/mcp`. |
| Tool/service authorization | Checks required tool scopes and service-operation permissions. |

Tool handlers should call operation-tagged services so IBeam audit logging can capture agent-driven actions.

## Data Storage

This API package does not create tables or repositories. Storage comes from Identity/API credential stores and the services called by registered tools.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- AI enablement kit: [`../IBeam.AI.Enablement/README.md`](../IBeam.AI.Enablement/README.md)
- Agent/API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)

Agents should treat MCP tools like API actions and keep durable work inside services.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Endpoint returns 401/403 | Authentication or authorization policy failed | Check API key auth registration and mapping policy. |
| Tool call is denied | Missing required scope/role/permission claim | Inspect API credential claims and `requiredScopes`. |
| Tenant context is empty | Tenant claim is missing | Ensure API credential authentication emits a tenant claim. |
| Service audit missing for tool action | Tool handler bypasses service executor/base service | Call an operation-tagged service method. |

## Version Notes

- Targets `net10.0`.
- Package version is assigned by the repository release workflow.
