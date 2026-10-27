# IBeam.Management.Api

Read the repository .agent/implementation-guide.md before changes.

Keep controllers thin and use IBeam response helpers. Require explicit management authentication schemes. Do not implement business permissions, secret validation or repository queries in controllers.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.

Application/connection controllers are optional explicit feature registrations. Never expose them through base assembly scanning. Connection DTO keys are write-only, body logging is disabled, responses are no-store, and automatic model-state responses must not echo values. No HTTP endpoint may accept arbitrary target URLs or secret references. Permissions and connection activation remain in services.

IBM-0097 explicitly allows typed diagnostic raw fields only when that module's host-owned Sanitization.Enabled is false and the host/deployment are supported non-Production contexts. The default/blank mode remains sanitized; no browser/request/claim may change it. This is the sole exception to older raw-message prohibition guidance: no audit JSON, actor/device/IP/tenant/entity columns or source/category/grant widening is allowed in either mode. Keep raw fields JSON-hidden in internal records and redacted in ToString; public raw objects must be clearly marked unsanitized. Advertise/verify module modes, bind target/portal tokens to the mode, and never attach payloads to audit or service logs. See docs/management/diagnostic-sanitization.md.

IBM-0106 portal tenant writes are explicit opt-in. Preserve Idempotency-Key and exact If-Match, route all rules through services and return encrypted tracking for uncertain outcomes. Polls require current authority; references are personal/app/connection-bound. No raw request/reference logging or machine/signing key output.

IBM-0108 portal onboarding create/retry use strict DTOs and the same protected operation tracking. A succeeded setup receipt is not business completion; use nested onboarding status. Retry requires current composite authority and accepts only empty JSON, never replacement recipient/policy/profile data. Keep these controllers opt-in and body logging disabled.

IBM-0111: portal provisioning/lifecycle use fixed scoped HTTPS paths, explicit independent grants, signed idempotency/If-Match, target-owned roles and safe reconstructed DTOs. Unknown writes return personal app-bound tracking; polls only read authoritative invitation state without decryption or mail. Generic processing success never implies acceptance. See docs/management/portal-customer-provisioning.md.

IBM-0112: membership and roles use caller ETags plus an atomic tenant-partition revision, target-owned immutable administrator roles and stable receipt IDs. Every native writer participates; protected legacy destructive writes fail closed. No implicit reactivation, global account/default/other-tenant edits or role restoration on re-enable. Customer request/refresh enforcement is required before capability registration; standard JWT uses live native versions, custom auth must explicitly integrate. Keep business eligibility in services and persistence fencing in stores. See docs/management/customer-membership-lifecycle.md.

IBM-0113: tenant status is an independent exact-version, idempotent command with required lifecycle hooks and explicit customer request/refresh enforcement. Preserve global users, memberships, defaults and business/billing data. No bootstrap reactivation. Recover only owned status transitions; release only known unapplied metadata reservations after concurrent writes. See docs/management/customer-tenant-status.md.

IBM-0114: onboarding policy keys, role templates and initializer selection are target-owned. Required initialization must reconcile durably by stable operation/tenant before invitations; Unknown never blindly initializes. Typed hook context excludes profiles and credentials. Identity-only remains independent of commercial modules. See docs/management/customer-onboarding-policy.md.

IBM-0124: owners.read and owners.transfer are independent opt-in capabilities; roles.assign never grants transfer. Preserve atomic native adapters, live enforcement, signed source/destination/snapshot versions and same-intent receipt recovery. Polling is read-only. Audit only scoped IDs and outcome. Existing grants remain unchanged; see docs/management/customer-ownership-api.md.

IBM-0127: optional licence status administration requires exact native versions, all-writer fencing and atomic immutable receipts. Keep restore bounded to the original runtime-eligible status plus target-owned policy. Volatile native providers are explicit Development/Testing fixtures only. Typed Management licence adapters and endpoints are opt-in; their signed dispatcher/client is separate. Unknown replies never retry mutations, and polls only read receipts. Preserve metadata-only audit and independent licence grants. See docs/management/license-status-management.md.
