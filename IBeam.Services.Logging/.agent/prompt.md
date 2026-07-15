# IBeam.Services.Logging Agent Prompt

You are working inside `IBeam.Services.Logging`.

Start with the root implementation guide at `.agent/implementation-guide.md`, then apply this package-specific guidance.

## IBeam Architecture Rules

Preserve the core IBeam boundary model:

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

API projects stay thin. APIs call services, capture service results, and use IBeam response/error helpers.

Services are the business boundary. Business logic, permissions, audit tags, validation, orchestration, logging, and error translation belong in services.

Repositories are persistence boundaries for one entity. Repositories should not call APIs, services, or other repositories.

Use stable operation names for service calls when this project adds service-level behavior. Align operation names with audit and permission rules when possible.

## Package Purpose

IBeam service audit/system logging support for entity adds, updates, deletes, archives, and direct developer logging calls.

Layer role: `Logging`.

## Public Surface

Keep the public surface focused on:

- System log models
- Audit/logging services
- Logging options and configuration policies
- Sinks for durable service change history

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Logging belongs to the framework service layer, not Identity.
- Capture database entity before/after JSON for data-change audits.
- Use ILogger for diagnostics and IBeam system logs for durable business/audit records.
- Keep per-service and per-operation opt-out/override behavior configurable.

- Treat IBeam system logs as durable audit/change records and ILogger as runtime diagnostics.
- Capture persisted entity before/after snapshots for data-changing operations when audit logging is enabled.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Services.Logging` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
