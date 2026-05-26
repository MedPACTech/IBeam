# IBeam AI Agent Instructions

You are building software on top of IBeam. Follow these rules strictly.

## Primary Objective

Produce maintainable, multi-tenant architecture that composes IBeam packages instead of bypassing them.

## Layer Ownership

1. API layer: transport only (routing, model validation, auth context extraction).
2. Service layer: all business rules, role/permission checks, orchestration, and policy decisions.
3. Repository layer: persistence only.
4. Provider layer: concrete store integrations (Azure Table, EF, etc.).

## Hard Rules

1. Do not call repository providers directly from controllers.
2. Do not place business rules in controllers or repositories.
3. Keep role and permission enforcement in service methods.
4. Do not leak provider entities into API contracts.
5. Respect connection-string cascade conventions.
6. Implement default-safe logging/error behavior if host does not customize sinks.

## Service Design Rules

1. Prefer one primary entity per service to limit complexity.
2. Services may call other services, but avoid cyclic dependency graphs.
3. If orchestration risks circular references, introduce a coordinator/facade service.
4. Keep shared business logic in dedicated domain services, not cross-calling repositories.

## Error and Logging Rules

1. Unhandled errors should flow through framework exception pipeline.
2. If no custom sinks exist, use built-in fallback sinks.
3. When provider-backed defaults are enabled, persist to `SystemErrors` and `SystemLogs`.

## Blueprint Selection

- For multi-tenant composition: `blueprints/component-multi-tenant-app.md`
- For identity package usage: `blueprints/package-identity.md`
- For repositories/provider composition: `blueprints/package-repositories.md`
- For communications (Twilio etc.): `blueprints/package-communications.md`
