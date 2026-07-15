# IBeam.Utilities Agent Prompt

You are working inside `IBeam.Utilities`.

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

General-purpose utilities shared across IBeam packages.

Layer role: `Core`.

## Public Surface

Keep the public surface focused on:

- Utility helpers
- Small reusable extensions
- Cross-cutting support types with no domain ownership

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Keep utilities small, stable, and dependency-light.
- Do not put business rules, service orchestration, or provider code here.
- Avoid turning utilities into a hidden dependency hub.
- Prefer moving cohesive behavior into the owning package instead of adding broad helpers.

- Keep this package cohesive and avoid turning it into a dependency hub.
- Put provider-specific, API-specific, and storage-specific behavior in the matching package instead of core abstractions.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Utilities` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
