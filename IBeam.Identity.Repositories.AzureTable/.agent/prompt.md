# IBeam.Identity.Repositories.AzureTable Agent Prompt

You are working inside `IBeam.Identity.Repositories.AzureTable`.

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

Azure Table repository implementation for IBeam Identity users, tenants, roles, credentials, OTP challenges, and indexes.

Layer role: `Repository`.

## Public Surface

Keep the public surface focused on:

- Azure Table identity repositories
- Azure Table identity entities
- Table naming and PartitionKey/RowKey conventions
- Identity schema migration/inspection helpers where appropriate

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- Persist and query identity entities; do not enforce business authorization here.
- Preserve IBeamIdentity table prefixes and existing key conventions.
- Document new fields and whether they are read-path, write-path, or audit metadata.
- Keep service logic in IBeam.Identity.Services.

- Keep storage key/query decisions explicit and documented.
- Let services translate repository errors into user-facing service results.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Identity.Repositories.AzureTable` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.

IBM-0105 adds IIdentityTenantConditionalStore. Management renames must use the caller-provided ETag and merge only framework name/update/LastOperationId fields; preserve consumer columns and never refresh away the original precondition. Legacy missing creation times project null. Reads never add metadata or rewrite rows.

IBM-0107 external invite Ensure uses a stable primary insert and validates immutable ownership/intent fields before repairing its token index. Never replace a redeemed/revoked primary during recovery. Token index reads must recheck the authoritative primary and exact token hash; stale indexes cannot restore old invitation authority. No schema creation or business orchestration in stores.

IBM-0110: invitation transitions require exact ETags, stable operation IDs and native verified acceptance claims before membership. No claim stealing or automatic resend after SMTP uncertainty. Keep private ciphertext separate from safe receipts/audit and enforce target-owned roles/rate/redirect policy. See docs/management/customer-invitation-lifecycle.md.

IBM-0112: membership and roles use caller ETags plus an atomic tenant-partition revision, target-owned immutable administrator roles and stable receipt IDs. Every native writer participates; protected legacy destructive writes fail closed. No implicit reactivation, global account/default/other-tenant edits or role restoration on re-enable. Customer request/refresh enforcement is required before capability registration; standard JWT uses live native versions, custom auth must explicitly integrate. Keep business eligibility in services and persistence fencing in stores. See docs/management/customer-membership-lifecycle.md.

IBM-0113: tenant status is an independent exact-version, idempotent command with required lifecycle hooks and explicit customer request/refresh enforcement. Preserve global users, memberships, defaults and business/billing data. No bootstrap reactivation. Recover only owned status transitions; release only known unapplied metadata reservations after concurrent writes. See docs/management/customer-tenant-status.md.

IBM-0122: store OwnerRoleIds/OwnershipProtocolVersion on the shared membership guard. All native membership writers advance the fence; opt-in or persisted owner protection blocks uncoordinated destructive writes and authority-dropping grants. Eligibility stays in services. Do not silently clear/rewrite owner policy or drop owner metadata from snapshots.

IBM-0123: ownership transfer is an optional trusted native adapter. Preserve atomic source/destination/shared-fence/receipt persistence, exact versions and same-intent recovery; never infer owner from the legacy tenant column or expose an arbitrary role grant. Read lookup must not initialize metadata. Permission and metadata audit belong to the caller. See docs/management/tenant-ownership-transfer.md.

IBM-0136: optional native Identity purge requires the original consumed plan/confirmed job and active journal lease before opaque native authorization. Preserve all native writer registration/local CAS fences, including delayed membership/token indexes and tenant-owned credential/consent/catalog stores. Keep global accounts/OTPs/sessions/unscoped account artifacts and other tenants intact; ordinary last-owner/admin checks remain active. Native controls use reserved !IBEAM|PURGE| rows and permanent job-owned tombstones; never reset them or expose raw hashes/profile data in Management DTOs/audit. App access/write enforcement is also required. Read-only marker lookup supports status after metadata removal and grants no cleanup rights. See docs/management/tenant-purge.md.
