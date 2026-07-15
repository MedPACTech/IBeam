# IBeam.AccessControl.Services Agent Prompt

You are working inside `IBeam.AccessControl.Services`.

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

Service-layer authorization and permission-management logic for IBeam operations, roles, subjects, agents, and optional dynamic grants.

Layer role: `Service`.

## Public Surface

Keep the public surface focused on:

- Access-control services
- Permission evaluation services
- Role/grant management service methods
- Operation-name based authorization helpers

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- This is the gatekeeper for access-control behavior.
- Apply hierarchy rules here: code/defaults, data-store grants, and configuration overrides.
- Keep support for users and agents explicit.
- Do not bind this service layer to one auth provider; consume subject context/claims abstractions.

- Prefer existing IBeam base service CRUD hooks before adding custom plumbing.
- Services may call other services for lookup data or rule evaluation, but avoid circular dependencies.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.AccessControl.Services` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
