# IBeam.Licensing Agent Prompt

You are working inside `IBeam.Licensing`.

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

Core licensing contracts, models, options, and abstractions for IBeam license validation and entitlement concepts.

Layer role: `Core`.

## Public Surface

Keep the public surface focused on:

- License models
- Entitlement contracts
- Licensing options and result types

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Keep this package storage- and API-neutral.
- Do not add web controllers or repository implementations here.
- Keep license rules expressible through service-layer implementations.
- Avoid application-specific SKUs or customer logic in core abstractions.

- Keep this package cohesive and avoid turning it into a dependency hub.
- Put provider-specific, API-specific, and storage-specific behavior in the matching package instead of core abstractions.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Licensing` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.

IBM-0127: optional licence status administration requires exact native versions, all-writer fencing and atomic immutable receipts. Keep restore bounded to the original runtime-eligible status plus target-owned policy. Volatile native providers are explicit Development/Testing fixtures only. Typed Management licence adapters and endpoints are opt-in; their signed dispatcher/client is separate. Unknown replies never retry mutations, and polls only read receipts. Preserve metadata-only audit and independent licence grants. See docs/management/license-status-management.md.

IBM-0128: seat reads/assign/unassign are independent explicit capabilities. Preserve native active-licence/capacity/customer-subject policy, one exact licence/seat aggregate revision across all writers, and immutable scope/intent receipts. A replay never consumes another seat or revokes a replacement. No membership/role/tenant status changes or customer impersonation. Bound snapshots to 2,000 safe assignments, never expose native subject/profile/provider text, and require durable production storage. Unknown replies only reconcile through authorized read-only lookups. See docs/management/seat-management.md.
