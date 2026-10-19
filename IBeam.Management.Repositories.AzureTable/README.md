# IBeam.Management.Repositories.AzureTable

IBM-0104 adds explicit target operation metadata and idempotent mutation-audit adapters: scoped point reads, atomic inserts, required ETags and append-once audit IDs. Provision tables in trusted setup; no runtime schema creation/deletion, raw idempotency key or customer request storage. See docs/management/customer-identity-operations.md.

IBM-0102 adds explicit TenantUsers and TenantInvites read adapters. They query one TEN|tenant-ID partition and USR| or INV| row prefix, select only approved membership/invitation metadata, and never access token-index partitions, profiles, OTP/session/user credential tables or runtime schemas. Read-only SAS is sufficient. See docs/management/customer-user-directory.md.

IBM-0101: AddIBeamManagementAzureTableCustomerTenantReader accepts an explicit existing target customer tenants table, including read-only SAS. Reads only TEN partition metadata and one provider page; never creates schema or selects consumer profiles. No portal/global TableClient is replaced. See docs/management/customer-tenant-directory.md.

Optional repository adapter for IBeam Management. It depends on core contracts and Azure Tables, with no Services, API or Identity dependency.

ManagementApiCredentials stores credential ID, application/environment, a versioned SHA-256 secret hash, explicit capabilities, Active/Revoked status, UTC creation/expiry/rotation/revocation and operator IDs. Raw API keys are never persisted. Point reads use DEPLOY|SHA256(JSON([application, environment])) and CRED|credentialId; no cross-deployment scans or secondary secret indexes. Updates compare the supplied ETag; concurrent rotation/revocation cannot overwrite one another.

Register AddIBeamManagementAzureTableCredentialStore(configuration), using IBeam:Management:Credentials:Storage:ConnectionString from a secret store and Storage:TableName (default ManagementApiCredentials). This connection is independently configured; it never falls back to customer/Identity storage. Alternatively supply a dedicated TableClient with a managed identity or table-scoped SAS. Registration never replaces a customer's TableClient.

Trusted deployment tooling creates the table explicitly using a privileged TableClient.CreateIfNotExistsAsync. Runtime registration and reads never provision schema. Runtime validation requires read permission only; trusted provisioning uses a separate write-capable credential/tool. Prefixes and table names alone are not storage authorization boundaries; choose separate accounts or properly scoped Azure credentials. The repository propagates infrastructure errors, and the caller fails closed.

Tests use private Azurite with random per-test tables, explicit setup/cleanup, real ETags and deployment-isolation checks. No cloud resources or production table operations are required.

## Existing SystemErrors (read-only)

AddIBeamManagementAzureTableSystemErrorReader(configuration) is in the Diagnostics namespace. It registers only ISystemErrorReadRepository. Set IBeam:Management:Diagnostics:Errors:Storage:ConnectionString from a secret store and Storage:TableName to the explicit full physical table name, for example SystemErrors. Both are required: no Identity/portal connection, default table, or prefix is inherited. Alternatively pass a dedicated read-only TableClient with managed identity or a table-scoped read SAS; customer TableClient registrations are preserved.

The adapter supports the existing ERR|yyyyMMdd partition and HHmmssfff|guid-N row-key format without rewriting data or copying it into portal storage. Each list call reads one bounded provider page using exact PartitionKey equality and OccurredAtUtc [from,to) predicates; details use point keys. It selects only keys, OccurredAtUtc, Source, TraceId and optional ManagementApplicationKey/ManagementEnvironment. It never selects raw Message/Exception/Path/Method payloads. Malformed timestamps are skipped; malformed attribution tags remain invalid rather than becoming legacy untagged records.

There is no table creation, schema hosted service or write method on ISystemErrorReadRepository. Trusted deployment tooling owns setup; runtime needs read permission only. A SHA-256 fingerprint of the physical account/table URI path binds continuation state without including connection credentials or SAS queries. Business source isolation, authorization, query/scan/time limits and protected token handling live in Management.Services, not this repository.

The three private Azurite integration cases prove unchanged row ETags, midnight/offset timestamp boundaries, stable pagination, exact server filters and selected columns, shared-source suppression, missing-record handling, no schema creation and successful reads with read-only SAS while writes are denied. They create/clean only random fixture tables.

## Existing SystemLogs (read-only)

AddIBeamManagementAzureTableSystemLogReader(configuration) is in the Diagnostics namespace and registers only ISystemLogReadRepository. Set IBeam:Management:Diagnostics:Logs:Storage:ConnectionString and Storage:TableName (the explicit full physical name, for example SystemLogs); nothing is inherited from IBeam:Logging:AzureTable or Identity prefixes. A dedicated read-only TableClient overload is also available.

Each query is one exact SYSTEM|DAY|yyyyMMdd partition with Category eq 'System' and an OccurredAtUtc range, plus optional exact Source/Level/TraceId equality filters. $select names only PartitionKey, RowKey, OccurredAtUtc, Category, IsSelectRollup, Source, Level, TraceId and the management tag columns. Rows are mapped only with an event row key (13-digit time plus 32 hex), Category System and no rollup flag. There is no table creation or write method.

## Application registry

AddIBeamManagementAzureTableApplicationStore(configuration) uses IBeam:Management:Applications:Storage:ConnectionString and TableName (default ManagementApplications), independently of Identity and customer storage. Alternatively supply a dedicated TableClient using a host-owned managed identity or scoped credentials. Provision the table explicitly during deployment; runtime paths never create schema.

Rows are partitioned by support organization, with APP|applicationId records and hashed KEY|app/environment uniqueness indexes. Creation adds both rows in one same-partition transaction; duplicate attempts cannot leave a partial app. Item reads and bounded pages always include organization equality. Updates require an explicit ETag and preserve immutable key/environment. Do not modify application identity fields directly through the repository; services own those invariants.

## Application connection credential metadata

AddIBeamManagementAzureTableApplicationCredentialStore(dedicatedTableClient) registers IManagementApplicationCredentialRepository. Provision ManagementApplicationCredentials explicitly beforehand. Each immutable row is scoped by support organization/application and credential ID, with secret-store reference, registered origin, verification time, actor and supported capabilities. No raw keys or hashes are stored here. References are excluded from entity JSON/ToString and all public connection responses. This is separate from target-side ManagementApiCredentials, which validate hashed incoming machine keys.

The application row's conditional ActiveCredentialId pointer selects a verified credential version; no atomic cross-table transaction is assumed. Failed pending versions can remain unreferenced. Runtime code never creates tables, enumerates secret material or deletes active versions.

## ManagementApplicationAccess

Register AddIBeamManagementAzureTableApplicationAccessStore(dedicatedTableClient). Explicitly provision ManagementApplicationAccess before runtime. Partition keys include management organization and application; user IDs are unique row keys. Bounded pages never query across those partitions or expose provider continuation state. Atomic create returns conflict for duplicate assignments, and explicit ETags protect every update/revoke. No wildcard replacement, automatic schema creation, authorization decision or call to another repository is added. Revocation is retained as metadata; no physical delete is used.

## Append-only ManagementAudit

AddIBeamManagementAzureTableAuditStore(dedicatedAuditTableClient) registers IManagementAuditRepository. AzureTableManagementAuditRepository adds generated entries without list/update/delete or runtime provisioning. Partition keys include the support organization (or target deployment marker) and UTC day. ExpiresUtc is metadata; Azure Table does not automatically expire rows. Use separate retention/archival tooling and privileges. A table-scoped Add-only SAS can keep the runtime append-only; support readers receive no storage credential or audit mutation API. Audit write timeout/failure policy belongs to the Management services sink, not this repository.

Deployment configuration, identity storage choices, MFA/bootstrap, onboarding, exact registrations/endpoints, network/proxy policy, secret rotation, audit retention/failure behavior and the deferred consumer host are covered by the [Management deployment handoff](https://github.com/MedPACTech/IBeam/blob/development/docs/management/deployment-and-handoff.md). [Source/history isolation](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-source-isolation.md) describes which historical rows may be exposed. Generated-package validation uses the package-only two-target smoke sample; IBM-0097 supplies independent host-owned errors/logs sanitization switches, safe by default and restricted to explicit non-Production contexts. See [sanitization settings](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-sanitization.md).
