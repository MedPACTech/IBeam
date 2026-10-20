# IBeam.Management.Identity.Target.Api

IBM-0106 adds signed read-only POST operations/resolve under original operation authority. Idempotency-Key stays in a bound header; body carries scope only. Installed endpoint plus usable lookup port advertises operationReceiptProtocolVersion=1. See docs/management/customer-tenant-portal-writes.md.

IBM-0105 adds opt-in AddIBeamManagementTenantAdministrationApi: signed POST tenants and exact-If-Match PATCH tenant name. Extra consumer/profile fields are rejected; missing conditional adapters advertise no update capability. Includes receipt routes. See docs/management/customer-tenant-administration.md.

IBM-0104 adds opt-in AddIBeamManagementIdentityOperationsApi: scoped signed receipt GETs, using original write authority or explicit onboarding.read. Mutation requests now require a signed Idempotency-Key; receipt infrastructure grants no business write capability. See docs/management/customer-identity-operations.md.

IBM-0102 adds independent AddIBeamManagementCustomerUserDirectoryApi and AddIBeamManagementCustomerInvitationDirectoryApi registrations. They expose tenant-scoped list/detail GET routes under /users and /invites with separate signed operations. No accept/resend/revoke/provision routes are installed here. See docs/management/customer-user-directory.md.

IBM-0101 adds optional AddIBeamManagementTenantDirectoryApi(configuration): signed tenants.read list/detail routes with protected paging and generic validation errors. Supply the existing target's dedicated read-only tenant table adapter and a separate directory protection key. See docs/management/customer-tenant-directory.md. Portal forwarding remains IBM-0103.

IBM-0100: future customer action endpoints also require a verified X-IBeam-Management-Actor assertion and a server-owned ManagementCustomerIdentityOperationAttribute. Install AddIBeamManagementDelegatedActors(configuration) with pinned public-key provider, atomic replay store and durable audit repository. GET /api/management-identity/info remains machine-only. No data routes are installed by this security foundation. See docs/management/customer-identity-delegation.md.

Optional customer Identity management module for the target application's host. Separate from the support-side IBeam.Management.Identity.Services package. See docs/management/customer-identity-discovery.md in the framework repository.

IBM-0098 supplies authenticated versioned discovery only. It does not expose customer tenant/user reads or provisioning. Explicitly register AddIBeamManagementCustomerIdentityTargetApi(configuration) in the target API host, supply IManagementApiCredentialRepository, and configure IBeam:Management:Diagnostics:ApplicationKey/Environment plus IBeam:Management:CustomerIdentity:IdentityRealmId and DedicatedApplicationRealm=true. Use the same stable realm ID across restarts; it identifies one dedicated app/environment Identity realm. Ambiguous multi-app shared realms are rejected.

Issue an independent management.identity.info machine credential through trusted existing provisioning tooling. The dedicated named scheme accepts X-IBeam-Management-Key only under /api/management-identity; it leaves customer authentication defaults unchanged. GET /api/management-identity/info returns installed capabilities after live credential checks. No target routes are discovered automatically by merely referencing this package. Missing query adapters cannot advertise customer read capabilities. No schema or customer Identity registration is performed by the discovery module.

The portal separately opts into AddIBeamManagementCustomerIdentityApi(configuration), preserving its support login and customer-store isolation. Existing diagnostics-only connection registration and public constructors remain available. Never send machine keys or target storage credentials to a browser.

Enterprise onboarding HTTP and portal registration, safe business status, scoped operation polling and explicit retry: [API contract](../docs/management/enterprise-onboarding-api.md).

Approved tenant roles and invitation-only customer provisioning: [registration and API contract](../docs/management/customer-provisioning.md). Catalog visibility uses the independent roles.read grant; no account/membership is created before customer verification.

IBM-0110 invitation resend/revoke uses exact versions, durable private delivery recovery and native acceptance claims. See [invitation lifecycle](../docs/management/customer-invitation-lifecycle.md).

IBM-0112 versioned tenant membership/role administration, concurrent final-administrator protection and required customer access enforcement: [lifecycle contract](../docs/management/customer-membership-lifecycle.md).

IBM-0113 conditional tenant activation/deactivation, lifecycle recovery and current customer access rejection: [status contract](../docs/management/customer-tenant-status.md).

Onboarding policy discovery and optional durable consumer initialization: [integration contract](../docs/management/customer-onboarding-policy.md).
