# IBeam.Api Agent Prompt

You are working inside `IBeam.Api`.

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

Shared API framework primitives for IBeam controllers, responses, error handling, and HTTP-facing conventions.

Layer role: `API`.

## Public Surface

Keep the public surface focused on:

- Base controllers
- Response/result helpers
- API error handling patterns
- Shared API DTO conventions

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Keep this package generic and reusable across IBeam features.
- Do not add domain-specific business rules here.
- Let services own authorization, audit, logging, validation, and orchestration.
- Preserve compatibility for API packages that depend on these base helpers.

- Controllers and endpoints should call one service method where possible and return the service result through IBeam response helpers.
- Do not put business rules, repository queries, permission decisions, or audit persistence in API controllers.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Api` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
