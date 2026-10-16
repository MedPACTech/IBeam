# IBeam.Management

Transport-independent contracts for support management. This package has no Identity, ASP.NET Core, service or repository dependency.

The first delivery provides named-human support identity and deployment-bound machine capability contracts. Public operations are `management.identity.read` and `management.diagnostics.info`. Deployment credential entity/repository/provisioning contracts now support explicit diagnostic permissions; optional adapters implement storage and verification. Safe system-error DTOs and opt-in list/detail API support are available; logs and staff/application assignments remain separate epic work.

Management context claims must be issued by trusted authentication after the appropriate membership/MFA or machine credential checks. They are not browser-supplied fields and must not be copied from arbitrary customer claims. Support organization identity is distinct from customer tenant identity.

`IManagementPrincipalProvider` supplies the authenticated principal. Services reject anonymous principals, mixed authenticated identities, missing/ambiguous context, wrong subject type, and missing operation grants. This is a service guard, not a replacement for token/key validation.

See `docs/planning/management-api-plan.md` in the source repository for the complete epic and acceptance criteria.

## Application registry and connection contracts

ManagementApplicationEntity represents organization-scoped app/environment metadata. ManagementApplicationCredentialEntity stores verified connection metadata plus a non-public secret reference. IManagementApplicationService and IManagementApplicationConnectionService expose administrator operations, without requiring IBeam Identity. IManagementSecretStore supports read-only references or writable stores; ManagementSecretValue and connection request models suppress secret serialization and redact ToString. No contracts depend on Azure, HTTP or Identity.
