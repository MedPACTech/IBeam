# IBeam.Tests.Repositories.OrmLite Agent Prompt

You are working inside `IBeam.Tests.Repositories.OrmLite`.

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

Test coverage for IBeam.Repositories.OrmLite.

Layer role: `Test`.

## Public Surface

Keep the public surface focused on:

- Focused unit and integration tests
- Behavioral coverage for public contracts
- Regression tests for bugs and schema-sensitive behavior

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Test the behavior of IBeam.Repositories.OrmLite without moving product code into the test project.
- Prefer deterministic unit tests; make real provider or external-service tests opt-in.
- Keep tests aligned with the API <-- DTO/model object --> Service <-- Entity --> Repository boundary.
- Name tests around expected behavior and failure modes, not implementation trivia.

- Test externally visible behavior first, then edge cases and regressions.
- Avoid shared mutable state between tests; use fresh fixtures/builders for each case.

## Dependency Boundaries

This test project may reference `IBeam.Repositories.OrmLite` and its required collaborators. Do not make production packages depend on test projects.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Use this project to prove the matching package behavior. Add tests here when changing public contracts, service rules, storage behavior, provider behavior, or bug fixes.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
