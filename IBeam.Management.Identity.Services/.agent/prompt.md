# IBeam.Management.Identity.Services
Read the root .agent/implementation-guide.md. This is an optional Identity adapter; do not introduce Identity dependencies into Management core/services/API.
Keep rules and trusted bootstrap in services, storage in existing Identity providers, and HTTP in a separate API adapter. Management hosts cannot share an unkeyed Identity container with customer hosts. Never expose bootstrap publicly or capture secrets in audit. Keep storage, authentication/session and unsafe enrollment isolation tests current.

Support onboarding reuses management Identity users/memberships/TenantInvites and only grants ManagementSupport. Email invitation tokens/passwords are write-only; no session may be issued before normal MFA login. Atomic redemption markers stay in the existing TenantInvites table. Routine disablement must reject self and any management administrator. Rebuild user/access-admin and delegation permissions from live catalog roles on every request/refresh.

IBM-0109 adds independent customer roles.read. Derive bounded operation/delegation limits from the explicit operation catalog, without auto-granting new names to administrators or reusing customer roles. All app-specific ceilings and live assignment checks remain required.

IBM-0124: owners.read and owners.transfer are independent opt-in capabilities; roles.assign never grants transfer. Preserve atomic native adapters, live enforcement, signed source/destination/snapshot versions and same-intent receipt recovery. Polling is read-only. Audit only scoped IDs and outcome. Existing grants remain unchanged; see docs/management/customer-ownership-api.md.

IBM-0126: preserve the immutable per-host ManagementOperationRegistry and original built-in overloads. Typed app descriptors are trusted code, with fixed GUID routes, bounded strict schemas, explicit grants and exact audit actions. Registration/discovery never grants authority or invokes handlers. Keep app data separate from Identity discovery; extension HTTP dispatch and commercial/data handlers require subsequent explicit modules. See docs/management/registered-operations.md.
