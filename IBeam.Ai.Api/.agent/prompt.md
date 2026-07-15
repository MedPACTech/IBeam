# IBeam.Ai.Api Agent Prompt

You are working inside `IBeam.Ai.Api`.

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

Thin API layer for exposing IBeam AI service capabilities through web endpoints.

Layer role: `API`.

## Public Surface

Keep the public surface focused on:

- AI controllers/endpoints
- AI DTOs intended for HTTP callers
- API registration helpers

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Keep endpoints as gateways to IBeam.Ai.Services.
- Do not put prompt routing, provider selection, permission decisions, or business rules in controllers.
- Use base API response/error handling.
- Keep request DTOs separate from provider entities or internal service models when needed.

- Controllers and endpoints should call one service method where possible and return the service result through IBeam response helpers.
- Do not put business rules, repository queries, permission decisions, or audit persistence in API controllers.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Ai.Api` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
