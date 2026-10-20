# IBeam.Management

IBM-0106 adds safe portal mutation/tracking contracts, typed operation lookup and optional operationReceiptProtocolVersion discovery. These are independent of grants and keep core free of Identity/HTTP dependencies. See docs/management/customer-tenant-portal-writes.md.

IBM-0105 adds typed tenant create/rename commands, safe operation/tenant results and separate provenance/external-reference metadata ports. No Identity or ASP.NET dependency is added to core. See docs/management/customer-tenant-administration.md.

Transport-independent contracts for support management. This package has no Identity, ASP.NET Core, service or repository dependency.

Customer Identity discovery and bounded directory contracts are in the CustomerIdentity namespace. IBM-0098 adds typed module/realm metadata only; it does not install tenant/user read or provisioning endpoints. See docs/management/customer-identity-discovery.md and the follow-on tenant/customer management backlog.

IBM-0099/0100 add explicit operation/delegation contracts and signed request-bound support actor interfaces. Assertion values and request body/path binding inputs are JSON-hidden and redacted in ToString. See docs/management/customer-identity-permissions.md and customer-identity-delegation.md. These contracts do not create customer principals or install data endpoints.

Contracts cover isolated human support identity, app/environment registration, verified target connections, per-user app assignments, an authorized selector, safe errors/logs, backend portal orchestration and append-only management audit. Optional packages provide HTTP, Identity, storage and secret-store integrations.

Management context claims must be issued by trusted authentication after the appropriate membership/MFA or machine credential checks. They are not browser-supplied fields and must not be copied from arbitrary customer claims. Support organization identity is distinct from customer tenant identity.

`IManagementPrincipalProvider` supplies the authenticated principal. Services reject anonymous principals, mixed authenticated identities, missing/ambiguous context, wrong subject type, and missing operation grants. This is a service guard, not a replacement for token/key validation.

See `docs/planning/management-api-plan.md` in the source repository for the complete epic and acceptance criteria.

## Application registry and connection contracts

ManagementApplicationEntity represents organization-scoped app/environment metadata. ManagementApplicationCredentialEntity stores verified connection metadata plus a non-public secret reference. IManagementApplicationService and IManagementApplicationConnectionService expose administrator operations, without requiring IBeam Identity. IManagementSecretStore supports read-only references or writable stores; ManagementSecretValue and connection request models suppress secret serialization and redact ToString. No contracts depend on Azure, HTTP or Identity.

Deployment configuration, identity storage choices, MFA/bootstrap, onboarding, exact registrations/endpoints, network/proxy policy, secret rotation, audit retention/failure behavior and the deferred consumer host are covered by the [Management deployment handoff](https://github.com/MedPACTech/IBeam/blob/development/docs/management/deployment-and-handoff.md). [Source/history isolation](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-source-isolation.md) describes which historical rows may be exposed. Generated-package validation uses the package-only two-target smoke sample; IBM-0097 supplies independent host-owned errors/logs sanitization switches, safe by default and restricted to explicit non-Production contexts. See [sanitization settings](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-sanitization.md).

Enterprise onboarding services and durable invitation recovery are documented in [enterprise onboarding](../docs/management/enterprise-onboarding.md). Target services are opt-in; portal/HTTP integration follows in IBM-0108.

Enterprise onboarding HTTP and portal registration, safe business status, scoped operation polling and explicit retry: [API contract](../docs/management/enterprise-onboarding-api.md).

Approved tenant roles and invitation-only customer provisioning: [registration and API contract](../docs/management/customer-provisioning.md). Catalog visibility uses the independent roles.read grant; no account/membership is created before customer verification.

IBM-0110 invitation resend/revoke uses exact versions, durable private delivery recovery and native acceptance claims. See [invitation lifecycle](../docs/management/customer-invitation-lifecycle.md).
