# IBeam.Ai.Services

`IBeam.Ai.Services` provides MCP protocol orchestration, tool registration, and default role/scope filtering for IBeam-backed applications.

```powershell
dotnet add package IBeam.Ai.Services
```

## When To Use This

- You want to register agent tools without wiring HTTP endpoints.
- You need `IAgentMcpService` to process MCP JSON-RPC requests.
- You want default tool access checks based on authenticated claims.
- You want AI tool calls to align with IBeam service policies and audit patterns.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Tool registration | `AgentToolRegistryBuilder` | Builds a catalog of agent tools. |
| Catalog | `IAgentToolCatalog` implementation | Serves the registered tool list. |
| MCP orchestration | `AgentMcpService` | Handles tool list/call requests. |
| Access policy | `DefaultAgentToolAccessPolicy` | Checks required scopes/roles/permissions against claims. |
| DI | `AddIBeamAiServices(...)` | Registers AI services, IBeam service policies, and service auditing. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

MCP tool handlers should behave like controller actions: bind tool arguments, call a service, and return the result. Business logic, permissions, licensing checks, logging, and validation should remain in services.

## Quick Start

```csharp
using IBeam.Ai;

builder.Services.AddIBeamAiServices(tools =>
{
    tools.AddTool(
        name: "app.work.list_cards",
        description: "List work cards visible to the current agent.",
        inputSchema: AgentToolSchemas.EmptyObject(),
        moduleKey: "work",
        requiredScopes: [ "api-scope:work" ],
        handler: async (context, arguments, ct) =>
        {
            var service = context.Services.GetRequiredService<IWorkItemService>();
            return await service.ListAsync(ct);
        });
});
```

For ASP.NET Core endpoint wiring, use `IBeam.Ai.Api`.

## Default Tool Access

`DefaultAgentToolAccessPolicy` allows authenticated callers when a tool has no required scopes. When `RequiredScopes` are present, it checks common claim types:

```text
role, roles, scope, scopes, scp, permission, permissions
```

Values may be comma- or space-separated.

## Custom Access Policy

Replace `IAgentToolAccessPolicy` when module access needs app-specific rules:

```csharp
builder.Services.AddScoped<IAgentToolAccessPolicy, AppAgentToolAccessPolicy>();
```

Example custom policy shape:

```csharp
public sealed class AppAgentToolAccessPolicy : IAgentToolAccessPolicy
{
    private readonly ILicenseAuthorizer _licenses;

    public AppAgentToolAccessPolicy(ILicenseAuthorizer licenses)
    {
        _licenses = licenses;
    }

    public async ValueTask<AgentToolAccessResult> CanAccessAsync(
        AgentToolContext context,
        AgentToolDefinition tool,
        CancellationToken cancellationToken = default)
    {
        if (context.TenantId is null || context.AgentKey is null)
        {
            return AgentToolAccessResult.Deny("Tenant and agent context are required.");
        }

        await _licenses.RequireEntitlementAsync(
            context.TenantId.Value,
            new LicenseSubject(LicenseSubjectTypes.Agent, context.AgentKey),
            $"mcp:{tool.ModuleKey}",
            cancellationToken);

        return AgentToolAccessResult.Allow();
    }
}
```

## Service Operations, Auditing, And Permissions

The AI services package registers IBeam service policies and service auditing. Tool handlers should call service methods tagged with `[IBeamOperation]`, such as `work.cards.list` or `pricing.save`, so the same audit and permission model applies to agent-driven work.

## Data Storage

This package does not create tables or storage. Any persistence comes from the services called by tool handlers.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- AI enablement kit: [`../IBeam.AI.Enablement/README.md`](../IBeam.AI.Enablement/README.md)
- Agent/API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)

Agents should avoid putting durable business rules in tool handlers.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Tool call denied | Missing required scope/role/permission claim | Verify API credential claims and `requiredScopes`. |
| Tool handler cannot resolve a service | Service not registered in DI | Register the domain service in the host application. |
| Tool bypasses audit | Handler calls repository directly | Route work through an operation-tagged service. |

## Version Notes

- Targets `net10.0`.
- Package version is assigned by the repository release workflow.
