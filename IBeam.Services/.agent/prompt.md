# IBeam.Services Agent Prompt

You are working inside `IBeam.Services`.

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

Core IBeam service-layer framework, including base CRUD patterns, service results, validation hooks, logging/audit integration points, and operation naming.

Layer role: `Service`.

## Public Surface

Keep the public surface focused on:

- Base service classes
- CRUD service abstractions
- Service result/error contracts
- Operation/audit/permission attributes and hooks

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- This package defines the service layer as the business boundary.
- Keep APIs and repositories from taking over service responsibilities.
- Support overrides for CRUD operations and blocking unsupported operations.
- Keep access-control and audit hooks generic so teams can use Identity or bring their own auth.

- Prefer existing IBeam base service CRUD hooks before adding custom plumbing.
- Services may call other services for lookup data or rule evaluation, but avoid circular dependencies.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Services` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
