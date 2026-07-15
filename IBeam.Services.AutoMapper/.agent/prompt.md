# IBeam.Services.AutoMapper Agent Prompt

You are working inside `IBeam.Services.AutoMapper`.

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

AutoMapper integration for IBeam services and DTO/entity mapping.

Layer role: `Mapping`.

## Public Surface

Keep the public surface focused on:

- Mapping profile helpers
- Service mapping extensions
- AutoMapper registration helpers

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Keep mapping configuration here, not business rules.
- Do not make repositories depend on AutoMapper.
- Use mappings to translate DTO/model/entity shapes cleanly across API/service/repository boundaries.
- Avoid hiding permission, audit, or validation decisions in mapping profiles.

- Keep mappings transparent and boring; do not hide validation, authorization, or persistence rules in profiles.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Services.AutoMapper` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
