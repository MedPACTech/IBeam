# IBeam.Communications Agent Prompt

You are working inside `IBeam.Communications`.

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

Core communication abstractions for email and SMS messaging across IBeam.

Layer role: `Core`.

## Public Surface

Keep the public surface focused on:

- IEmailService and SMS abstractions
- Message models
- Shared communication options
- Validation and provider exception types

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Keep this package provider-neutral.
- Do not add SMTP, SendGrid, Twilio, Azure-specific transport logic here.
- Keep validation and shared sender/destination rules consistent for all providers.
- Let service packages decide when messages may be sent and what operation name applies.

- Keep this package cohesive and avoid turning it into a dependency hub.
- Put provider-specific, API-specific, and storage-specific behavior in the matching package instead of core abstractions.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Communications` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
