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

IBM-0122: ownership is an explicit optional native policy, independent of administrator protection. Keep new ownership contracts provider/HTTP independent, preserve existing public constructors, and never infer ownership from OwnerUserId or support claims.

IBM-0123: ownership transfer is an optional trusted native adapter. Preserve atomic source/destination/shared-fence/receipt persistence, exact versions and same-intent recovery; never infer owner from the legacy tenant column or expose an arbitrary role grant. Read lookup must not initialize metadata. Permission and metadata audit belong to the caller. See docs/management/tenant-ownership-transfer.md.

IBM-0136: optional native Identity purge requires the original consumed plan/confirmed job and active journal lease before opaque native authorization. Preserve all native writer registration/local CAS fences, including delayed membership/token indexes and tenant-owned credential/consent/catalog stores. Keep global accounts/OTPs/sessions/unscoped account artifacts and other tenants intact; ordinary last-owner/admin checks remain active. Native controls use reserved !IBEAM|PURGE| rows and permanent job-owned tombstones; never reset them or expose raw hashes/profile data in Management DTOs/audit. App access/write enforcement is also required. Read-only marker lookup supports status after metadata removal and grants no cleanup rights. See docs/management/tenant-purge.md.

IBM-0139: preserve the separate metadata-only membership event type (no AuthLifecycleEventBase Metadata/destination/profile fields), immutable original commit IDs/sequence/actor and logical revisions. Rejected/no-op/replayed writes create no new event; atomic owner transfer is one two-membership envelope. The shared projector is not an emitter or proof of durable storage. Native atomic outbox/dispatch is IBM-0140; never substitute a post-commit callback for the provider transaction. See docs/management/membership-lifecycle-events.md.

IBM-0140: keep membership(s), shared sequence/revision guard, immutable event and operation index atomic in the tenant partition and join native purge fences. Event enrollment is explicit/sticky and bound to configured app/environment/realm; every current writer must participate. Original logical revisions, actor/time/IDs and event payload never change during retries. Native/verified support actor capture is metadata only, never request-body authority. Explicit compatible receiver required; default auth publisher cannot claim delivery. Ordered ETag leases, bounded timeout/retry/dead-letter and exact-version trusted repair never repeat membership mutation. Purge fences suppress late delivery/metadata recreation; consumer projections must join app fences. No runtime DDL, implicit grant or raw provider/body audit. Full feature legacy regression belongs at IBM-0141.
