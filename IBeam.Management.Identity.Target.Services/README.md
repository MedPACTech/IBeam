# IBeam.Management.Identity.Target.Services

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
