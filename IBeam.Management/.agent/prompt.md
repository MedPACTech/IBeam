# IBeam.Management

Read the repository .agent/implementation-guide.md before changes.

Keep this package transport independent. No Identity, ASP.NET Core, services or repository references. Management organization, application/environment and customer tenant are distinct scopes.

Enterprise onboarding contracts separate safe business status from generic processing receipts and private encrypted recovery input. Mail delivery or a succeeded setup receipt never implies customer acceptance. Do not expose private journal input, invite tokens or support impersonation through DTOs.

Operation receipts may carry an optional safe onboarding projection. Missing projections convey no acceptance result. Retry requests are strict empty objects; changed enterprise input requires a new explicitly authorized intent.

IBM-0109 adds roles.read independently of assignment, bounded approved-role contracts and invitation-only provisioning. Core stays Identity/provider independent. Provisioning receipts identify the tenant; native invite IDs are separate. Never put private recipient/token recovery input into public operation/audit contracts.

Do not leak secrets or raw customer payloads into DTOs, audit snapshots, exception messages or logs. Preserve API -> Services -> Repository boundaries. Build focused tests for authentication substitution, scope isolation, denied access and unsafe diagnostic content. Keep README registration and scope accurate. The separate IBeam.Admin.API host and its rename are deferred.

IBM-0097 explicitly allows typed diagnostic raw fields only when that module's host-owned Sanitization.Enabled is false and the host/deployment are supported non-Production contexts. The default/blank mode remains sanitized; no browser/request/claim may change it. This is the sole exception to older raw-message prohibition guidance: no audit JSON, actor/device/IP/tenant/entity columns or source/category/grant widening is allowed in either mode. Keep raw fields JSON-hidden in internal records and redacted in ToString; public raw objects must be clearly marked unsanitized. Advertise/verify module modes, bind target/portal tokens to the mode, and never attach payloads to audit or service logs. See docs/management/diagnostic-sanitization.md.

IBM-0110: invitation transitions require exact ETags, stable operation IDs and native verified acceptance claims before membership. No claim stealing or automatic resend after SMTP uncertainty. Keep private ciphertext separate from safe receipts/audit and enforce target-owned roles/rate/redirect policy. See docs/management/customer-invitation-lifecycle.md.

IBM-0111: portal provisioning/lifecycle use fixed scoped HTTPS paths, explicit independent grants, signed idempotency/If-Match, target-owned roles and safe reconstructed DTOs. Unknown writes return personal app-bound tracking; polls only read authoritative invitation state without decryption or mail. Generic processing success never implies acceptance. See docs/management/portal-customer-provisioning.md.

IBM-0112: membership and roles use caller ETags plus an atomic tenant-partition revision, target-owned immutable administrator roles and stable receipt IDs. Every native writer participates; protected legacy destructive writes fail closed. No implicit reactivation, global account/default/other-tenant edits or role restoration on re-enable. Customer request/refresh enforcement is required before capability registration; standard JWT uses live native versions, custom auth must explicitly integrate. Keep business eligibility in services and persistence fencing in stores. See docs/management/customer-membership-lifecycle.md.

IBM-0113: tenant status is an independent exact-version, idempotent command with required lifecycle hooks and explicit customer request/refresh enforcement. Preserve global users, memberships, defaults and business/billing data. No bootstrap reactivation. Recover only owned status transitions; release only known unapplied metadata reservations after concurrent writes. See docs/management/customer-tenant-status.md.

IBM-0114: onboarding policy keys, role templates and initializer selection are target-owned. Required initialization must reconcile durably by stable operation/tenant before invitations; Unknown never blindly initializes. Typed hook context excludes profiles and credentials. Identity-only remains independent of commercial modules. See docs/management/customer-onboarding-policy.md.

IBM-0115: provenance is evidence-based optional target metadata: support-provisioned, self-service or unknown. Trusted self-service recorder has no HTTP endpoint and never infers origin or changes native signup. Reads enrich one bounded page without writes; missing evidence stays unknown. See docs/management/tenant-provenance.md.

IBM-0122: OwnershipProtectionProtocolVersion is optional safe native membership metadata, with zero omitted. It grants no authority and introduces no Identity/provider dependency into core Management.

IBM-0124: owners.read and owners.transfer are independent opt-in capabilities; roles.assign never grants transfer. Preserve atomic native adapters, live enforcement, signed source/destination/snapshot versions and same-intent receipt recovery. Polling is read-only. Audit only scoped IDs and outcome. Existing grants remain unchanged; see docs/management/customer-ownership-api.md.

IBM-0126: preserve the immutable per-host ManagementOperationRegistry and original built-in overloads. Typed app descriptors are trusted code, with fixed GUID routes, bounded strict schemas, explicit grants and exact audit actions. Registration/discovery never grants authority or invokes handlers. Keep app data separate from Identity discovery; extension HTTP dispatch and commercial/data handlers require subsequent explicit modules. See docs/management/registered-operations.md.

IBM-0130: export uses a single explicitly compiled native route/DTO descriptor and same-operation read-only status alias. Keep independent management.tenants.export grants, signed tenant/job scope, strict safe inventory/status projection and private artifact/customer-delivery ports. Default disabled; required native providers and durable jobs fail closed at startup. No generic native-prefix proxy or support artifact download. See docs/management/tenant-export.md; framework worker/store and portal export integration are later cards.

IBM-0131: preserve the dedicated fully scoped ETag journal, immutable same-intent fingerprint, frozen inventory and separate authenticated encrypted artifact column. Leased native hooks must reconcile by full scoped job identity and fence publication/delivery; unknown outcomes pause rather than auto-retry. Customer artifact access is server-only, requires current customer authorization twice, and rejects Management principals. Cancellation/expiry deny access immediately; physical cleanup is separate, bounded and exact-job, including historical disabled bindings. Background scans are bounded metadata-only recovery. No runtime schema creation, raw provider errors or ciphertext/body audit. See docs/management/tenant-export.md for accepted-command semantics and key/provider retention.
