# IBeam.Ai

`IBeam.Ai` contains the core AI agent, tool, and MCP contracts shared by IBeam-backed applications.

```powershell
dotnet add package IBeam.Ai
```

## When To Use This

- You need agent tool definitions in shared code.
- You are building MCP tools without taking an ASP.NET Core dependency.
- You want a common `AgentToolContext` for user, tenant, API credential, and agent identity data.
- You are implementing your own MCP orchestration or access policy.

Most host applications should reference `IBeam.Ai.Api`, which brings the service and core packages transitively.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Tool definition | `AgentToolDefinition`, `AgentToolHandler` | Describes a callable agent tool and its handler. |
| Tool context | `AgentToolContext`, `IAgentToolContextFactory` | Carries principal, services, agent key, tenant ID, and API credential ID. |
| Tool catalog | `IAgentToolCatalog` | Exposes registered tools. |
| Access policy | `IAgentToolAccessPolicy`, `AgentToolAccessResult` | Allows hosts to decide whether a tool can run. |
| MCP service contract | `IAgentMcpService` | Handles MCP JSON-RPC payloads. |
| MCP models | `McpJsonRpcResponse`, `McpToolDefinition`, `McpToolCallResult`, error codes | Shared protocol models. |
| Schema helpers | `AgentToolSchemas` | Convenience methods for JSON input schemas. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

Agent tools are another API surface. A tool handler should call a service. It should not bypass the service layer to call repositories or embed business permissions directly.

## Code Example

```csharp
var tool = new AgentToolDefinition(
    name: "hubbsly.work.list_cards",
    description: "List work cards visible to the current agent.",
    inputSchema: AgentToolSchemas.EmptyObject(),
    handler: async (context, arguments, ct) =>
    {
        var service = context.Services.GetRequiredService<IWorkItemService>();
        return await service.ListAsync(ct);
    },
    moduleKey: "work",
    requiredScopes: [ "api-scope:work" ]);
```

## Access Control

The core package only defines the policy contract. The default service package policy checks authenticated claims for required scopes/roles/permissions. Applications can replace `IAgentToolAccessPolicy` to call IBeam AccessControl, Licensing, Identity, or a custom authorization system.

## Data Storage

This package does not create tables, repositories, containers, or buckets.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- AI enablement kit: [`../IBeam.AI.Enablement/README.md`](../IBeam.AI.Enablement/README.md)
- Agent/API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)

Agents should keep tool handlers thin and route real work through operation-tagged services.

## Version Notes

- Targets `net10.0`.
- Package version is assigned by the repository release workflow.
