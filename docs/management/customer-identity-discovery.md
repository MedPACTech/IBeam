# Customer Identity management discovery

IBM-0098 adds an optional customer-management module, separate from support Identity. Customer directories, writes, grants and delegated actor assertions are delivered by subsequent cards; no customer records are exposed by this foundation.

## Packages and configuration

- Target services: IBeam.Management.Identity.Target.Services, which may compose existing customer Identity services in subsequent cards.
- Target HTTP: IBeam.Management.Identity.Target.Api; no dependency on the diagnostics API or support Identity adapter.
- Portal contracts/services/API stay in IBeam.Management, IBeam.Management.Services and IBeam.Management.Api, without customer Identity dependencies.

Register AddIBeamManagementCustomerIdentityTargetApi(configuration) only inside the customer's target host. Supply IManagementApiCredentialRepository and trusted credential setup separately. Configure IBeam:Management:Diagnostics:ApplicationKey and Environment for existing credential binding, plus IBeam:Management:CustomerIdentity:IdentityRealmId (a stable non-empty GUID) and DedicatedApplicationRealm=true. This attests that the customer Identity realm belongs to this app/environment alone. A naming prefix is not a database permission boundary. Shared multi-app realms are not supported without a future trusted app-scope adapter. Startup rejects missing/ambiguous realm configuration. Registration does not install Identity schemas or customer authentication endpoints.

The module installs its own IBeam.Management.CustomerIdentity.Credential named authentication scheme and accepts only X-IBeam-Management-Key under /api/management-identity. Existing customer defaults and diagnostics authentication are unchanged. Issue management.identity.info via existing trusted provisioning tooling. Runtime checks credential app/environment, ID/version, status, expiration and operation before returning metadata. Info uses a machine credential; subsequent customer data operations additionally require verified human delegation.

## Discovery contract

GET /api/management-identity/info uses the standard success/data envelope. Data fields: applicationKey, environment, identityRealmId, realmScope (dedicated-app), protocolVersion (1), capabilities. Foundation targets advertise management.identity.info only. Future HTTP modules register IManagementCustomerIdentityCapability with a recognized operation and its required adapter service type; capabilities are advertised only when the adapter resolves and the credential has that operation.

Portal opt-in: AddIBeamManagementCustomerIdentityApi(configuration), in addition to AddIBeamManagementApi and the existing application/credential/access repositories, support-state provider and secret store. It adds GET /api/management/applications/{applicationId}/customer-management. An active app assignment is required even for administrators; discovery grants no customer permissions. In this foundation existing diagnostic assignments can inspect availability. IBM-0099 adds independently assignable customer operations.

The result has status available, not-installed, incompatible or unavailable; info is present only for validated available metadata. This endpoint reports the last verified connection snapshot, not a live customer read. Retest and replace/rotate the connection to persist changed module metadata. No customer profiles or secrets are stored in that snapshot.

## Connection compatibility

Without portal opt-in the existing IManagementTargetConnectionProbe and 2.15.0 diagnostics-only workflow remain unchanged. Opt-in installs IManagementTargetProfileProbe and independent bounded discovery at /api/management-diagnostics/info and /api/management-identity/info. An Identity-only target returns 404 for the diagnostics route. At least one usable module must validate before connection activation. Combined targets must agree exactly on app/environment. Unknown customer protocol versions, malformed or oversized customer metadata suppress only customer availability when valid diagnostics exist. The strict diagnostics v1/sanitization parser is unchanged; invalid diagnostics are never accepted by falling back to another module. No redirects, proxies, private-IP exceptions, unpinned DNS or insecure TLS are introduced.

Existing connection entities remain readable. New verified versions optionally store typed customer module metadata alongside existing capabilities and secret references, using an additive Azure Table column. No automatic migration or permissions upgrade occurs. Public existing constructors and interfaces remain usable.

Customer DTOs and bounded directory query ports are transport/provider neutral and contain only framework Identity metadata. No IdentityUser objects, hashes, credentials, OTPs, sessions, security stamps, raw profiles or consumer business records appear. Query ports are not implementations or public routes.
