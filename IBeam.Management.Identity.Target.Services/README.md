# IBeam.Management.Identity.Target.Services

IBM-0106 adds scoped receipt lookup by the original idempotency identity without customer payloads or writes. It reconstructs the existing operation ID under live authority and never executes mutation handlers. See docs/management/customer-tenant-portal-writes.md.

IBM-0105 adds ManagementTenantAdministrationService over existing Identity lifecycle services, durable operation receipts and separate source/reference/phase metadata. Stable IDs and conditional writes recover incomplete consumer extensions. No customer membership/default tenant/role/token is assigned. See docs/management/customer-tenant-administration.md.

IBM-0104 adds durable operation reservation, canonical keyed fingerprints, ETag claims, stable resource IDs, reconciliation and repeatable metadata audit delivery. Business handlers must use conditional downstream writes; leases alone cannot provide exactly-once behavior. See docs/management/customer-identity-operations.md.

IBM-0102 adds independent read-only customer membership/invitation directory services with exact tenant scope, protected paging, bounded role/contact metadata and no global profile lookup. Invitation expiry is projected without writes. See docs/management/customer-user-directory.md for source and field semantics.

IBM-0101: IManagementTenantDirectoryService performs authenticated metadata-only tenant reads through an entity-focused read repository. One bounded provider page per request; empty filtered pages may have continuation. No full-directory materialization, customer profiles or writes. See docs/management/customer-tenant-directory.md.

IBM-0100: AddIBeamManagementDelegatedActors(configuration) validates short-lived signed actors bound to the exact target/request/machine credential. Customer services must call IManagementCustomerIdentityTargetAccessService.RequireAsync before store access; direct calls without a verified scoped actor fail. Mutation assertions require a dedicated atomic replay store. No customer identity or membership is fabricated. See docs/management/customer-identity-delegation.md.

IBM-0099 adds a target-owned EnabledOperations allowlist under IBeam:Management:CustomerIdentity. It defaults empty (discovery only). Advertising an operation also requires its explicit machine capability, installed adapter and any composite prerequisites. See docs/management/customer-identity-permissions.md. Customer data endpoints require subsequent delegated actor and reader/provisioning cards.

Optional customer Identity management module for the target application's host. Separate from the support-side IBeam.Management.Identity.Services package. See docs/management/customer-identity-discovery.md in the framework repository.

IBM-0098 supplies authenticated versioned discovery only. It does not expose customer tenant/user reads or provisioning. Explicitly register AddIBeamManagementCustomerIdentityTargetApi(configuration) in the target API host, supply IManagementApiCredentialRepository, and configure IBeam:Management:Diagnostics:ApplicationKey/Environment plus IBeam:Management:CustomerIdentity:IdentityRealmId and DedicatedApplicationRealm=true. Use the same stable realm ID across restarts; it identifies one dedicated app/environment Identity realm. Ambiguous multi-app shared realms are rejected.

Issue an independent management.identity.info machine credential through trusted existing provisioning tooling. The dedicated named scheme accepts X-IBeam-Management-Key only under /api/management-identity; it leaves customer authentication defaults unchanged. GET /api/management-identity/info returns installed capabilities after live credential checks. No target routes are discovered automatically by merely referencing this package. Missing query adapters cannot advertise customer read capabilities. No schema or customer Identity registration is performed by the discovery module.

The portal separately opts into AddIBeamManagementCustomerIdentityApi(configuration), preserving its support login and customer-store isolation. Existing diagnostics-only connection registration and public constructors remain available. Never send machine keys or target storage credentials to a browser.

Enterprise onboarding services and durable invitation recovery are documented in [enterprise onboarding](../docs/management/enterprise-onboarding.md). Target services are opt-in; portal/HTTP integration follows in IBM-0108.

Enterprise onboarding HTTP and portal registration, safe business status, scoped operation polling and explicit retry: [API contract](../docs/management/enterprise-onboarding-api.md).

Approved tenant roles and invitation-only customer provisioning: [registration and API contract](../docs/management/customer-provisioning.md). Catalog visibility uses the independent roles.read grant; no account/membership is created before customer verification.

IBM-0110 invitation resend/revoke uses exact versions, durable private delivery recovery and native acceptance claims. See [invitation lifecycle](../docs/management/customer-invitation-lifecycle.md).

IBM-0111 selected-app role catalog, customer provisioning and invitation lifecycle forwarding plus read-only receipt status: [portal API contract](../docs/management/portal-customer-provisioning.md).

IBM-0112 versioned tenant membership/role administration, concurrent final-administrator protection and required customer access enforcement: [lifecycle contract](../docs/management/customer-membership-lifecycle.md).

IBM-0113 conditional tenant activation/deactivation, lifecycle recovery and current customer access rejection: [status contract](../docs/management/customer-tenant-status.md).

Onboarding policy discovery and optional durable consumer initialization: [integration contract](../docs/management/customer-onboarding-policy.md).

Optional trusted tenant provenance and existing signup compatibility: [integration contract](../docs/management/tenant-provenance.md).

Role replacement hardening (IBM-0121): the selected provisioning policy must authorize removals as well as assignments. Omitting an out-of-policy Owner or another privileged role is denied before membership writes; inconsistent legacy role names require trusted native normalization. This does not require the independent `roles.read` grant. See the [membership lifecycle contract](../docs/management/customer-membership-lifecycle.md).
