# IBeam.Identity Agent Prompt

You are working inside `IBeam.Identity`.

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

Core identity contracts, models, options, and abstractions for IBeam authentication, users, tenants, roles, OTP, and credentials.

External tenant invitation contracts are trusted server adapters, not authentication endpoints. No support GUID becomes a customer inviter. Stable creation and safe delivery are separate; Management supplies durable delivery claims and customer acceptance remains in existing Identity workflows.

ApprovedRoleNames on external invitation requests is a JSON-hidden trusted host-policy result. Never bind it from a public request. Recipients and tokens remain JSON-hidden and redacted; only native verified acceptance grants membership.

Layer role: `Core`.

## Public Surface

Keep the public surface focused on:

- Identity models and options
- User/tenant/role contracts
- OTP and credential abstractions
- Identity result and error contracts

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Keep storage implementations out of this core package.
- Keep API controllers and HTTP concerns in IBeam.Identity.Api.
- Do not make core framework packages depend on Identity unless the feature truly belongs here.
- Keep access-control concepts aligned with IBeam.AccessControl instead of duplicating permission maps.

- Keep this package cohesive and avoid turning it into a dependency hub.
- Put provider-specific, API-specific, and storage-specific behavior in the matching package instead of core abstractions.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Identity` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.

IBM-0110: invitation transitions require exact ETags, stable operation IDs and native verified acceptance claims before membership. No claim stealing or automatic resend after SMTP uncertainty. Keep private ciphertext separate from safe receipts/audit and enforce target-owned roles/rate/redirect policy. See docs/management/customer-invitation-lifecycle.md.

IBM-0112: membership and roles use caller ETags plus an atomic tenant-partition revision, target-owned immutable administrator roles and stable receipt IDs. Every native writer participates; protected legacy destructive writes fail closed. No implicit reactivation, global account/default/other-tenant edits or role restoration on re-enable. Customer request/refresh enforcement is required before capability registration; standard JWT uses live native versions, custom auth must explicitly integrate. Keep business eligibility in services and persistence fencing in stores. See docs/management/customer-membership-lifecycle.md.

IBM-0113: tenant status is an independent exact-version, idempotent command with required lifecycle hooks and explicit customer request/refresh enforcement. Preserve global users, memberships, defaults and business/billing data. No bootstrap reactivation. Recover only owned status transitions; release only known unapplied metadata reservations after concurrent writes. See docs/management/customer-tenant-status.md.
