# IBeam.Communications.Sms.AzureCommunications Agent Prompt

You are working inside `IBeam.Communications.Sms.AzureCommunications`.

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

Azure Communication Services SMS provider for the shared IBeam SMS abstraction.

Layer role: `CommunicationProvider`.

## Public Surface

Keep the public surface focused on:

- Azure SMS service
- Azure SMS options
- DI registration for Azure SMS transport

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Configuration

Expected configuration section:

```text
IBeam:Communications:Sms:AzureCommunications
```

When adding settings, update the options object, startup validation, `README.md`, and any sample `application.json` file.

## Implementation Rules

- Keep this package as an SMS provider adapter.
- Use shared SMS validation/contracts from IBeam.Communications.
- Do not add API controllers, repositories, or identity-specific checks.
- Wrap provider failures consistently for calling services.

- Keep provider SDK details inside this package and expose provider-neutral communication abstractions to callers.
- Provider failures should be wrapped in IBeam communication exceptions.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Communications.Sms.AzureCommunications` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.
