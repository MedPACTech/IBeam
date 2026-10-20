# Customer tenant directory

IBM-0101 adds opt-in target customer tenant list/detail reads. The portal directory forwarder is IBM-0103; this card does not expose customer data from portal storage.

## Target setup

Use the optional customer Identity target API and verified delegated actor registrations from customer-identity-delegation.md. Supply the target application's existing dedicated Identity tenants TableClient with AddIBeamManagementAzureTableCustomerTenantReader(dedicatedCustomerTenantsTable). A read-only SAS is sufficient; the reader never creates schemas, updates rows, replaces a global TableClient or infers a customer connection from support identity settings.

Call AddIBeamManagementTenantDirectoryApi(configuration). Configure a separate random 32-byte base64 IBeam:Management:CustomerIdentity:TenantDirectory:ProtectionKey; default token lifetime is 15 minutes (allowed 1-60), timeout 10 seconds (allowed 1-30). The key is server-owned, JSON-hidden and must come from environment/secret storage. Registration fails startup for missing adapters, target access/delegation dependencies or invalid key/options. Explicitly enable management.identity.tenants.read in the target EnabledOperations policy and issue it on the machine credential alongside management.identity.info.

DedicatedApplicationRealm=true is an operator attestation that the supplied table belongs to the configured application/environment/customer Identity realm. Support and customer Identity containers/stores remain separate. Ambiguous multi-app shared Identity storage is still rejected; no unverified tenant/app tag is treated as trustworthy scope. There is no support-organization directory or consumer profile repository fallback.

## Endpoints and contracts

- GET /api/management-identity/tenants?pageSize=50&status=Active&namePrefix=Acme
- GET /api/management-identity/tenants/{tenantId}

Both require the dedicated machine scheme, server-mapped tenants.read operation and verified signed support actor. Direct service calls use the same access checks through IServiceOperationExecutor. Reads recheck access before returning. Existing customer/default auth cannot satisfy this scheme.

List data is an array of tenantId, name, status, createdUtc, updatedUtc and version. The envelope has pageSize and continuationToken at the top level. Detail uses the standard data object. No Identity user, roles, customer extension profile, payload, business fields or raw provider cursor is returned.

Page size defaults to 50 and must be 1-100. Status is exactly Active or Disabled, or absent. Name prefix is at most 100 characters, trimmed and matched using invariant uppercase/ordinal prefix comparison. Missing legacy Status means Active, matching the existing Identity store. Missing legacy NormalizedName needs no backfill: matching derives normalization from the actual tenant name. Malformed keys/statuses, control-character/blank/oversized names (over 512 characters) and invalid row types are suppressed.

## Bounded queries and continuations

Azure reads only partition TEN using the existing GUID-D row keys and explicit Name/NormalizedName/Status/CreatedAt/UpdatedAt plus storage metadata selection. Each list request reads at most one provider page of at most 100 rows; no GetAllAsync or full-directory materialization, per-row cross-repository lookup, index creation or schema write occurs.

The initial provider deliberately applies status/name filters within that bounded page rather than relying on populated historical normalized-name/status indexes. Results may be empty with continuation. Clients must continue while a token exists and preserve page size/filters; empty data alone does not mean the search is complete. Large sparse searches may need multiple requests. Results are ordered by existing Azure row-key order, not tenant name, and do not promise a snapshot across concurrent changes.

Continuation tokens use authenticated AES-GCM with fixed expiry and bind app/environment, Identity realm, verified support organization/user, target credential, physical source hash and normalized filters/page size. Another actor/source/filter cannot replay a token; expiry never extends while paging. Source identity excludes SAS query secrets, so refreshing a read-only SAS does not change the source. Changing source, realm or protection key invalidates old cursors.

Detail is a single bound TEN/tenant-ID point read. Missing/malformed resources return safe 404; invalid queries or cursors return generic 400; provider failures/timeouts return generic 503. Model binding never echoes submitted filter values. Metadata-only audit records actor, app, machine credential and requested resource ID without names, filters, cursor, table URI, payload or exception text.
