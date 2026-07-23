# AgentUsers Architecture

AgentUsers make AI agents first-class tenant principals while keeping API credentials as the machine-authentication secret. Identity owns the actor model, API credentials own key material and key lifecycle, and AI/MCP consumes the authenticated agent context for tool discovery and calls.

## Core Model

| Entity | Owner | Purpose |
|---|---|---|
| `ApiCredentials` | `IBeam.Identity` | Existing machine key record: raw key issuance, secret hash, expiration, revocation, scopes, roles, and last-used metadata. Non-AI software can keep using this directly. |
| `AgentUsers` | `IBeam.Identity` | Tenant-scoped AI actor/profile such as `Front End Codex Dev`, with `agentType` like `codex`, `claude`, `grok`, or `custom`. |
| `AgentUserCredentials` | `IBeam.Identity` | Binding table that assigns one API credential to one AgentUser. This lets an agent have separate keys per environment, rotation window, team, or integration. |

## Authentication Flow

1. A caller sends `X-API-Key: {raw-api-key}` or `Authorization: ApiKey {raw-api-key}`.
2. IBeam validates the API credential and builds the normal API credential principal.
3. If the credential is bound to an active AgentUser, Identity enriches the principal with `agent_user_id`, `agent_user_name`, `agent_type`, and `agent_key`.
4. REST endpoints, service operations, audit logging, and MCP tools read the same claims.
5. Internal logs can keep `api_credential_id` for traceability, but external self-identity surfaces prefer the AgentUser identity.

## REST Shape

`GET /api/agentusers/me` returns the active agent identity for the current API-key principal. Tenant-admin routes manage profiles and credential bindings:

- `GET /api/tenants/{tenantId}/agentusers`
- `POST /api/tenants/{tenantId}/agentusers`
- `GET /api/tenants/{tenantId}/agentusers/{agentUserId}`
- `PUT /api/tenants/{tenantId}/agentusers/{agentUserId}`
- `POST /api/tenants/{tenantId}/agentusers/{agentUserId}/credentials`
- `DELETE /api/tenants/{tenantId}/agentusers/{agentUserId}/credentials/{credentialId}`

## MCP Shape

MCP stays API-key authenticated. IBeam registers a small built-in tool, `agentusers.me`, so MCP-capable agents can ask “who am I in this tool system?” The response returns safe agent context, scopes, and tools, while avoiding raw secrets and keeping credential identifiers internal unless the host deliberately exposes them.

## Extension Point

Consuming apps can extend AgentUsers the same way they extend Users and Tenants: store additional app-owned profile rows keyed by `tenantId` and `agentUserId`, or use metadata for lightweight fields such as team, project, provider workspace, model family, evaluation policy, or cost center.
