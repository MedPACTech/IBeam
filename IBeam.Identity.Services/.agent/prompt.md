# IBeam.Identity.Services Agent Prompt

You are working inside `IBeam.Identity.Services`.

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

Service-layer implementation for identity users, tenants, roles, OTP, API credentials, sign-in workflows, and identity business rules.

Layer role: `Service`.

## Public Surface

Keep the public surface focused on:

- Identity services
- Tenant/user/role orchestration
- OTP challenge and lockout logic
- Credential and claims service behavior

When adding public types, make sure the owning layer is correct and update the README when registration, configuration, or consumer-facing behavior changes.

## Implementation Rules

- This is the identity gatekeeper; keep account, role, tenant, OTP, and credential rules here.
- Use repositories only for persistence and lookup.
- Coordinate with IBeam.AccessControl for permission maps instead of rebuilding a second access-control system.
- Use stable operation names for auditable/permissioned identity actions.

- Prefer existing IBeam base service CRUD hooks before adding custom plumbing.
- Services may call other services for lookup data or rule evaluation, but avoid circular dependencies.

## Dependency Boundaries

Keep dependencies pointed inward toward abstractions and shared framework packages. Avoid adding references that reverse the intended flow from API to service to repository.

Identity should remain optional unless this project is explicitly an Identity package. Bring-your-own-auth should remain possible for access-control-aware services.

## Testing Guidance

Add or update focused tests in `IBeam.Tests.Identity.Services` when behavior changes. If that test project does not exist yet, use the closest existing test project for this package family.

Provider or infrastructure integration tests should be opt-in when they require real cloud resources, SMTP servers, SMS providers, databases, or local machine configuration.

## Packaging

The `.agent/prompt.md` file is included in the NuGet package by the repository-level packaging rules. Keep it useful for agents consuming the package from source or from the package artifact.

IBM-0105 external tenant administration reuses Identity creation/lifecycle services with an optional conditional store. Preserve null customer AuthUserId, RequireLifecycleDelivery and RedactFailureDetails for external operations. These flags convey no permission; target Management authorization is separate. Recovery hooks can receive null previous state and must be idempotent by operation ID.

IBM-0107 adds an optional trusted external invitation adapter to TenantInviteService. It never creates membership before customer acceptance, never fabricates a customer inviter and never returns tokens/links. External password setup requires an email verification challenge. Preserve ordinary invite/self-signup behavior. The Management journal owns ETag delivery claims; do not rewrite invitation state after SMTP or silently retry ambiguous delivery.

IBM-0109 external callers may supply a bounded host-approved role-name result for selected target-local IDs. This is a trusted server adapter parameter, never an HTTP/claim-controlled allowlist. Native role scope/active/name checks still run before persisting. Legacy default external approval remains Admin/Owner; generic provisioning policy is enforced by the target Management service.

IBM-0110: invitation transitions require exact ETags, stable operation IDs and native verified acceptance claims before membership. No claim stealing or automatic resend after SMTP uncertainty. Keep private ciphertext separate from safe receipts/audit and enforce target-owned roles/rate/redirect policy. See docs/management/customer-invitation-lifecycle.md.
