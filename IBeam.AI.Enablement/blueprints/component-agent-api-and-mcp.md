# Blueprint: Agent API Access and MCP Tool Surfaces

## Objective

Guide applications that want AI agents to access an IBeam-backed API by using API credentials for authentication and, when needed, an MCP endpoint for curated tool calls.

Use `IBeam.Ai.Api` for ASP.NET Core endpoint wiring, `IBeam.Ai.Services` for MCP protocol handling and default role/scope filtering, and `IBeam.Ai` for shared contracts and models. The host application still owns concrete domain tools and business services.

## Core Decision

API-key authentication and MCP are separate concerns:

1. API keys answer who the agent is and what tenant/roles/scopes it has.
2. REST endpoints answer how agents call the normal application API.
3. MCP endpoints answer how MCP-capable clients discover and call curated tools.

Do not require API-key callers to exchange the key for a JWT. JWT access tokens are for human/user sessions created through OTP, password, OAuth, or refresh flows. API credentials are service identities and should authenticate directly.

## Authentication Pattern

Agents call IBeam-backed APIs with one of these headers:

```http
X-API-Key: {raw-api-key}
```

or:

```http
Authorization: ApiKey {raw-api-key}
```

Do not send API credentials as Bearer tokens:

```http
Authorization: Bearer {raw-api-key}
```

Bearer tokens are JWT access tokens and will be validated as JWTs.

## API Credential Model

Use IBeam API credentials for machine and agent callers:

1. Create one credential per agent, integration, environment, or automation boundary.
2. Store only the raw key in the client secret store; IBeam stores the hash.
3. Assign API-safe role/scope names such as `API`, `api-scope:work`, `tool:mcp`, `agent:codex`, or app-specific module scopes.
4. Use `agentKey` to identify the calling agent in service workflows.
5. Avoid human-management roles such as `Owner`, `Administrator`, and `Admin`.

Host applications can configure the raw key prefix:

```json
{
  "IBeam": {
    "Identity": {
      "ApiCredentials": {
        "KeyPrefix": "hbk"
      }
    }
  }
}
```

## REST API Access

If an agent only needs normal application operations, prefer regular REST endpoints protected by IBeam authorization.

Service-layer rules:

1. Put tenant, role, permission, and module checks in services.
2. Read agent identity from claims such as `api_agent_key`, `agent_key`, `api_credential_id`, `tid`, and role claims.
3. Keep controllers thin and let services enforce business rules.
4. Use API credential role/scope names for agent-safe authorization.

## MCP Tool Surface

Add an MCP endpoint only when the product needs MCP-capable clients to discover and invoke tools through the MCP protocol.

Good MCP candidates:

1. Curated workflows such as `work.list_cards`, `work.create_card`, or `contacts.log_communication`.
2. Operations where the tool description and input schema help agents use the API safely.
3. Cross-service workflows that should not expose the full REST surface.
4. Read or write operations that need module-specific access checks.

Avoid making MCP a generic mirror of every REST endpoint. MCP should be a deliberate tool facade.

## MCP Endpoint Requirements

An MCP endpoint should:

1. Be protected by IBeam API-key auth, usually through the default authorization policy or explicit API-key-aware policy.
2. Support the MCP JSON-RPC methods needed by clients, typically `initialize`, `ping`, `tools/list`, and `tools/call`.
3. Filter `tools/list` by the current agent's allowed modules/scopes.
4. Re-check authorization in `tools/call`; never rely only on filtered discovery.
5. Resolve the current agent key from the authenticated claims.
6. Return structured tool results, not only text.
7. Log denied and invalid tool calls without leaking secrets.

## Recommended MCP Layering

Keep MCP protocol handling separate from application business logic:

1. API/controller endpoint:
- Accept HTTP request.
- Require authorization.
- Parse JSON payload.
- Delegate to an MCP service.

2. MCP service:
- Use `IBeam.Ai.Services` to handle JSON-RPC protocol shape.
- Register tool metadata, input schemas, and handlers through `AddIBeamAiMcp`.
- Let `IBeam.Ai.Services` dispatch tool calls and convert service results into MCP tool results.

3. Application services:
- Own business logic.
- Enforce tenant/module/role/permission rules.
- Read current tenant and agent context from authenticated claims or a host-provided context abstraction.

4. Access-control service:
- Map agent keys, credential ids, roles, scopes, and modules.
- Answer questions such as `CanAccessModule(agentKey, moduleKey)`.

## Sample Tool Registry Shape

Use app-owned tool definitions. A tool registration should include:

```text
name
module or scope key
description
input schema
handler
```

Example names:

```text
hubbsly.work.list_projects
hubbsly.work.list_cards
hubbsly.work.create_card
hubbsly.contacts.list_contacts
hubbsly.contacts.log_communication
```

Use stable, namespaced tool names so clients can distinguish product areas and avoid collisions.

## Authorization Flow

For REST:

1. Agent sends `X-API-Key`.
2. IBeam validates the raw key.
3. IBeam creates a credential principal with tenant, credential, agent key, and role claims.
4. Controller/service executes if authorization succeeds.

For MCP:

1. Agent sends `X-API-Key` to `/api/mcp` or the app's chosen MCP route.
2. IBeam validates the raw key.
3. MCP `tools/list` returns only tools allowed for the agent.
4. MCP `tools/call` validates the requested tool and checks module/scope access again.
5. The MCP service calls normal application services.

## Implementation Checklist

1. Confirm API credentials are enabled and configured.
2. Create API-safe role/scope names for agent capabilities.
3. Issue credentials per agent/integration and store raw keys securely.
4. Protect normal REST endpoints with IBeam authorization.
5. Add MCP only for curated tool workflows.
6. Ensure the MCP route accepts API-key authentication, not JWT-only auth.
7. Filter tool discovery by agent permissions.
8. Re-check permissions on every tool call.
9. Add tests for API-key REST access, MCP `tools/list`, allowed tool calls, denied tool calls, and invalid Bearer usage.

## Anti-Patterns

1. Exchanging API keys for JWTs when the caller is a service identity.
2. Sending `ibk_` or other raw API keys as Bearer tokens.
3. Exposing all REST endpoints as MCP tools without curation.
4. Trusting MCP `tools/list` filtering as the only authorization check.
5. Putting tenant or module authorization only in controllers.
6. Giving agent credentials human admin roles.
