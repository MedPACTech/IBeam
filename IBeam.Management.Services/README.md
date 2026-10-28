# IBeam.Management.Services

IBM-0126 shares the per-host registered operation catalog across credential validation, live app assignments, support delegation and audit allowlists. Optional registered-operation capability discovery checks installed handlers and explicit live machine grants; it installs no dispatch endpoint or implicit human permission. See [registered operations](https://github.com/MedPACTech/IBeam/blob/development/docs/management/registered-operations.md).

Customer target discovery preserves validated optional native membership ownership-protection and operation-receipt protocol metadata without granting additional operations. See [owner protection and compatibility](../docs/management/tenant-ownership-protection.md).

IBM-0106 adds bounded signed tenant mutations and stateless encrypted operation references. A fresh authorization budget follows target timeouts; no generic write retry or tenant payload persistence is introduced. Lookup is a scoped read-only target request. See docs/management/customer-tenant-portal-writes.md.

IBM-0105 extends delegated request binding to include an optional If-Match digest. Version changes cannot be substituted under a signed mutation proof. Target tenant creation/rename is documented in docs/management/customer-tenant-administration.md; portal write forwarding follows separately.

IBM-0104 adds safe selected-app operation receipt forwarding. Non-GET delegated assertions bind the Idempotency-Key as well as exact method/path/body. See docs/management/customer-identity-operations.md.

IBM-0100: AddIBeamManagementActorDelegation(configuration) enables server-only short-lived RS256 support actor assertions. Supply a dedicated signing-key provider and durable metadata audit repository; configure explicit issuer/target audiences. Browser-facing APIs never return assertions or keys. See docs/management/customer-identity-delegation.md.

IBM-0099: independent management.identity customer operation names are now valid machine capabilities and app assignments. Composite operation checks, per-app delegation ceilings, effective selector/discovery projection and IManagementCustomerIdentityAuthorizationService are documented in docs/management/customer-identity-permissions.md. Existing diagnostic credentials/assignments gain nothing automatically.

IBM-0098 adds optional CustomerIdentity module discovery through AddIBeamManagementCustomerIdentity(configuration). IManagementTargetProfileProbe negotiates diagnostics and customer Identity independently; the legacy probe and constructors remain available. Machine credentials may explicitly include management.identity.info for the optional target discovery endpoint; this confers no directory or write access. Portal services remain independent of customer Identity. See docs/management/customer-identity-discovery.md.

Service boundary for management operations. Register `services.AddIBeamManagementServices()` and supply a trusted scoped `IManagementPrincipalProvider`, or use an API module which supplies the HTTP principal provider.

`ManagementIdentityService.GetCurrentAsync` requires a named authenticated human, one nonempty management organization ID and `management.identity.read`. `ManagementDiagnosticsInfoService.GetAsync` requires a management credential ID, exact application/environment binding and `management.diagnostics.info`.

Both methods execute through `IServiceOperationExecutor`. They use dedicated management guards inside the operation rather than generic customer-tenant owner/admin checks (`Permission=false` explicitly disables that unrelated tenant-role evaluator). Removing the optional generic authorizer cannot bypass these guards. The same operation names feed audit execution. Durable management audit/actor correlation is a later feature; production hosts must configure their durable audit sink and policy.

Configure `ManagementDiagnosticsOptions` (application key and environment) for a target info service. The target API module binds and validates these settings at startup.

Optional bounded/safe error reading is described below; logs, user/application assignments and the outbound application client are separately scoped. Optional login/MFA support is in IBeam.Management.Identity.Services/Api; these general services do not depend on Identity.

## Deployment credentials

Register AddIBeamManagementCredentialValidation(configuration) for runtime read-only verification, and supply IManagementApiCredentialRepository (for example IBeam.Management.Repositories.AzureTable). IBeam:Management:Diagnostics:ApplicationKey and Environment bind every operation to the local deployment. Registration does not install Identity or change customer authentication.

Keys are ibmgmt_v1.<credentialId in N format>.<32 random bytes encoded base64url>. Only a domain-separated, versioned SHA-256 hash is stored and checked with CryptographicOperations.FixedTimeEquals. This fast hash is for high-entropy generated credentials, never human passwords. Legacy tenant keys, JWTs and different format versions are rejected. Validation rereads current status, expiry, deployment and capabilities without caching. Infrastructure failures propagate and fail closed.

Capabilities are explicit: management.diagnostics.info, management.diagnostics.errors.read and management.diagnostics.logs.read. No wildcard, owner/admin shortcut or implied Errors/Logs grants exist. Defining a capability does not expose its diagnostic endpoint; readers are later features.

Trusted provisioning tooling alone calls AddIBeamManagementCredentialProvisioning(configuration). Set IBeam:Management:Credentials:Provisioning:Enabled=true only there, and replace the default-deny IManagementCredentialOperatorProvider with trusted host/operator authorization. RequireAuthorizedOperatorAsync must validate an operator independently of customer/portal roles and return a nonempty stable operator ID. No public HTTP issue/rotate/revoke route is installed. Runtime hosts register validation only and use read-only storage permissions.

Resolve IManagementCredentialProvisioningService and call IssueAsync(explicitCapabilities, expiry), RotateAsync(credentialId), or RevokeAsync(credentialId). MaximumLifetimeDays defaults to 90 and accepts 1-365. Rotation retains ID, capabilities and original expiry; it never reactivates revoked/expired credentials. Conditional persistence rejects concurrent changes instead of retrying a stale rotation over a revocation. Issue/rotate return a ManagementIssuedCredential whose ApiKey property must be handed directly to a secret store. That property and stored hash are excluded from JSON and redacted from ToString. Audit operations contain metadata only; never add request/entity/return snapshots. Operator IDs are persisted with lifecycle timestamps. Durable audit delivery/correlation remains the later audit feature.

## Target authentication and operation access

The optional Diagnostics API adapter registers ManagementCredentialAuthenticationService and ManagementDiagnosticsAccessService. The former creates only a machine principal after key/network validation; the latter requires the exact persisted operation grant and rechecks status, expiry, deployment and record version before every diagnostic query. The original three-argument info-service constructor remains available for BYO-auth; DI selects the live four-argument guard when installed by the credential adapter. Keep future error/log readers behind IManagementDiagnosticsAccessService.RequireAsync inside their IServiceOperationExecutor operation.

ManagementCredentialNetworkPolicy accepts configured CIDR ranges and a server-observed IPAddress; HTTP/proxy trust belongs to the host. No arbitrary request headers, customer roles or implied wildcard grants are accepted. Authentication/authorization audit records include credential ID, operation and safe request correlation, with no secret/hash snapshots. Existing host audit context providers are preserved.

## Bounded SystemErrors reader (IBM-0083)

Register AddIBeamManagementSystemErrorReader(configuration) from IBeam.Management.Services.Diagnostics and supply ISystemErrorReadRepository plus a live IManagementDiagnosticsAccessService. The persisted credential API adapter supplies the latter. For a target using that adapter, registration is:

```csharp
services.AddIBeamManagementAzureTableCredentialStore(configuration);
services.AddIBeamManagementDiagnosticsCredentialApi(configuration);
services.AddIBeamManagementAzureTableSystemErrorReader(configuration);
services.AddIBeamManagementSystemErrorReader(configuration);
```

The repository extension is in IBeam.Management.Repositories.AzureTable.Diagnostics. The HTTP authentication extension is in IBeam.Management.Diagnostics.Api.Authentication. This reader registration adds no error HTTP routes and does not advertise error-reading capability; safe DTO projection and routes are available through the opt-in IBM-0084 error API module below. IBeam.Admin.API stays deferred.

IBeam:Management:Diagnostics:Errors settings:
- SourceMode is required: DedicatedDeployment with DedicatedSourceConfirmed=true, or TaggedShared with TrustedTagsConfirmed=true.
- Dedicated mode is an operator assertion that this physical table contains only the configured application/environment. It permits legacy untagged rows but rejects any explicit mismatched/partial/malformed tags.
- TaggedShared requires exact ManagementApplicationKey and ManagementEnvironment columns from a trusted writer. Existing IBeam error rows have neither tag and are suppressed. The reader does not derive attribution from Source, Path, client headers or tenant text, modify the writer, or backfill historical rows. Operators must secure tag writers; field names alone do not prove trustworthy attribution.
- ProtectionKey is a dedicated secret containing exactly 32 random bytes in base64. No key is generated implicitly or inherited from Identity. Configure the same key across instances/restarts; replacing it invalidates outstanding IDs/cursors.
- DefaultWindowHours=24, MaximumWindowHours=168 (hard maximum 168).
- DefaultPageSize=50, MaximumPageSize=100 (hard maximum 1000).
- ScanBudgetPerPage=500 returned storage rows, MaximumStorageRequestsPerPage=16 diagnostic provider-page calls (maximum 32).
- RequestTimeoutSeconds=10 (maximum 30), TokenLifetimeMinutes=30 (maximum 60).

Resolve IManagementSystemErrorReader and call ReadAsync(DiagnosticQuery). FromUtc/ToUtc normalize to a UTC [from, to) interval; future end times beyond five minutes of clock tolerance are rejected. Query defaults are frozen into the first protected continuation. Subsequent requests can omit the original fields; explicitly changed bounds/page size are rejected. Iteration visits exact ERR|yyyyMMdd partitions in ascending date and natural storage row-key order. Legacy offset timestamps retain their existing row keys; filter by OccurredAtUtc, not inferred row-key time.

The reader checks Errors.Read through the live access service before every diagnostic query, including subsequent provider pages. Scan/request/time limits bound each call; a filtered or empty page may still contain NextCursor, which the client should continue. These limits bound returned rows and requests, not Azure's internal work evaluating filters inside one partition. No full-table or cross-application scan is used. Pagination follows the provider's continuation semantics rather than a database snapshot; concurrent inserts before an already visited key are not replayed.

AES-256-GCM authenticates and encrypts native continuations and record identifiers. Tokens bind purpose, system-errors record type, app/environment, physical source, isolation mode and the normalized query. Their fixed expiry is not renewed by paging. GetAsync(recordId, optionalOriginalQuery) performs a point read after authorization and token validation; a supplied query must match the protected one. Missing/removed or newly mismatched records return null. Invalid/replayed tokens return a safe DiagnosticTokenException; out-of-bound/conflicting queries return DiagnosticQueryException.

SystemErrorReadPage and SystemErrorRecord are internal repository/service results, not public DTOs. Source/TraceId remain untrusted metadata, are JSON-ignored, and must pass an allowlisted projection before public use. Message, Exception, Path, Method and customer payload columns are never selected by this adapter. No row keys, provider cursors, tokens, entities or query/response snapshots enter audit options. Operation names use management.diagnostics.errors.read through IServiceOperationExecutor; the host retains its durable audit/error middleware policy.

## Safe public error projection (IBM-0084)

AddIBeamManagementSystemErrorService registers ISystemErrorReaderService/SystemErrorReaderService over the existing internal reader. The HTTP error module calls this extension automatically. Both ListAsync and GetAsync enforce live Errors.Read before invoking the internal reader, and both execute through IServiceOperationExecutor under management.diagnostics.errors.read. No request/entity/return snapshots are attached to audit operations.

SystemErrorProjection has a fixed catalog: exact RequestPipeline gets a fixed approved source and summary; all other source text becomes generic System metadata. It never uses stored Message/Exception/Path/Method. Correlation is HMAC-SHA256 with a domain-separated subkey derived from the configured error protection key, and input bound to app/environment plus a trace ID of at most 128 characters. Public DTOs receive an opaque c1_ pseudonym, never the raw trace. This is a per-key diagnostic correlation contract; it does not expose a customer's identifier or promise a shared log correlation scheme.

Invalid and missing detail IDs both become ManagementDiagnosticNotFoundException. Invalid list continuations/queries retain their safe typed exceptions. Unexpected read failures become ManagementDiagnosticUnavailableException without raw message/inner text; only exception type is eligible for the public service's warning log. The internal reader translates storage failures before its own audit pipeline can capture unsafe provider messages. Cancellation is preserved. HTTP maps these outcomes to safe standard envelopes; no detailed-error host setting restores raw provider text.

Info capability reporting accepts optional IEnumerable<IManagementDiagnosticsCapability>; optional HTTP modules add their own implementations. Empty/base registration still advertises only info. Capability advertisement does not bypass any operation grant.

## System log reader and projection (IBM-0085)

AddIBeamManagementSystemLogService(configuration) registers the bounded internal IManagementSystemLogReader plus the public ISystemLogReaderService/SystemLogProjection; AddIBeamManagementSystemLogReader registers only the internal reader. Supply ISystemLogReadRepository and a live IManagementDiagnosticsAccessService exactly as for errors. SystemLogReaderOptions (IBeam:Management:Diagnostics:Logs) mirrors the error options with its own protection key and adds AllowedSources.

The reader follows the error contract: 24-hour default window, frozen continuation, page/scan/request/time budgets, live Logs.Read before every provider read, encrypted cursors and IDs bound to system-logs/app/environment/source binding/source mode, and dedicated-vs-tagged source trust. Source, level and trace filters are validated before storage, pushed down as exact filters, rechecked on every returned row and frozen into the cursor. Operations execute as management.diagnostics.logs.read under IServiceOperationExecutor without query/entity/return snapshots. The default SystemLogProjection omits message/detail text; it emits the fixed source catalog, a normalized level and a keyed c1_ correlation pseudonym distinct from the error pseudonym.

## Application administration

AddIBeamManagementApplications registers IManagementApplicationService. All methods enforce the human management context and management.applications.admin before storage access, execute through IServiceOperationExecutor and sanitize unexpected provider failures before audit. No customer role implies this permission. Register a host-owned IManagementPrincipalProvider and isolated IManagementApplicationRepository; registration never grants diagnostic access.

POST creates enabled metadata; PATCH supplies complete mutable name/origin/enabled configuration and an expected version. Application key/environment remain immutable. Approved outbound destinations and connection secrets belong to the separate connection module.

## Verified application connections

AddIBeamManagementConnections(configuration) supplies IManagementApplicationConnectionService and an HTTPS info probe. All connection operations go through IServiceOperationExecutor with metadata-only audit. Unexpected SDK, HTTP, DNS and provider failures are translated before audit; keys, secret references, response bodies and request snapshots are omitted.

ApprovedOrigins under IBeam:Management:Connections is an explicit host-owned list. Only registered root HTTPS origins on port 443 are called. The fixed GET /api/management-diagnostics/info sends X-IBeam-Management-Key. Redirects, proxies, cookies and arbitrary request headers/Host overrides are disabled. DNS answers must all be public; special/private/link-local/loopback/documentation/transition addresses fail closed. The socket connects to the checked IP with normal TLS certificate/SNI verification for the original hostname, preventing resolution changes between checking and connecting. There is no private-network exception switch.

The response is bounded to 32 KiB and must be a successful IBeam envelope using protocol 1, exact registered applicationKey/environment and known info/errors/logs capabilities (info mandatory). Raw target messages are never surfaced. TimeoutSeconds bounds secret persistence/read, probe and activation; internal deadlines become safe timeout failures and caller cancellation propagates.

### Secret-store choices

IManagementSecretStore stores keys outside table rows. AddIBeamManagementKeyVaultSecrets in the optional IBeam.Management.Secrets.AzureKeyVault package supplies writable, versioned secret storage using a dedicated host-created SecretClient and managed identity. No vault is provisioned by this framework.

AddIBeamManagementEnvironmentSecrets(options => options.Bindings.Add(new(orgId, appId, "qurvia-production", "QURVIA_MANAGEMENT_KEY"))) supplies read-only environment references. Bindings are trusted host configuration scoped to org/app. After registration, trusted administrative tooling calls ManagementApplicationConnectionService.ActivateEnvironmentReferenceAsync(appId, expectedVersion); it resolves that binding, verifies it and activates it. There is no HTTP reference-binding endpoint. PUT submissions are rejected in this mode; the framework never changes process environment variables.

ManagementApplicationConnectionService coordinates the entity-focused application service and credential repository. Verified credential metadata is append-only; the app's ActiveCredentialId is the authoritative pointer. Cross-table transactions are not assumed: an activation conflict can leave a verified, unreferenced row/secret. Preserve active versions and perform retention cleanup through explicit operator tooling.

Implementation references: [SocketsHttpHandler](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler?view=net-10.0), [TLS/SNI](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-sni), [IANA IPv4 special ranges](https://www.iana.org/assignments/iana-ipv4-special-registry/), [IANA IPv6 special ranges](https://www.iana.org/assignments/iana-ipv6-special-registry/).

## Application assignment enforcement

AddIBeamManagementApplicationAccess registers IManagementApplicationAccessService. Supply an assignment repository and live IManagementSupportUserStateProvider (optional Management Identity adapter or host-owned implementation). The application service owns the IManagementApplicationStateProvider lookup; assignment services never query Identity or application repositories directly.

RequireAsync(appId, ManagementOperations.ErrorsRead/LogsRead) rechecks all live states and the current user's exact assignment on every read. There is no administrator bypass, wildcard, cache, claim-only grant or customer-role fallback. Organization and user IDs come from one authenticated human identity. Missing, disabled, revoked, mismatched or malformed records deny.

Administration requires a live administrator and explicit access-admin permission; per-capability delegation claims bound both old and proposed grants. Updates and revocations are compare-and-swap operations with no blind retries. Rows retain creation/change/revocation times and actors. Revoking disabled/deleted recipients is permitted because the already-scoped row cannot restore access; creating/regranting requires a live enabled recipient. All operations run through IServiceOperationExecutor with safe exception translation and no entity/request/diagnostic snapshots.

## Portal diagnostic orchestration (IBM-0091)

Opt in with AddIBeamManagementPortalDiagnosticsApi(configuration). Register a support authentication scheme with AddIBeamManagementApi, live support-user/assignment/application/credential repositories, and a server-side secret store. The four GET endpoints under /api/management/applications/{applicationId}/system-errors and /system-logs (including /{id}) call IManagementDiagnosticsService -> IManagementDiagnosticConnectionResolver -> IApplicationDiagnosticsClient. Access is checked before key lookup and again before/after diagnostic retrieval. Administrators also need an explicit app assignment. The target info response must match registered app/environment and requested capability on every read.

Set IBeam:Management:PortalDiagnostics:ProtectionKey to a secret 32-byte base64 key, stable across portal replicas. TimeoutSeconds defaults to 10 (1–30); TokenLifetimeMinutes defaults to 15 (1–60); MaximumResponseBytes defaults to 262144 (32768–1048576). Supply the same explicit Connections:ApprovedOrigins policy used by connection onboarding. Only fixed diagnostic GET routes are sent; keys stay in the dedicated backend header. Redirects/proxies/cookies/automatic trace forwarding are disabled; checked public DNS addresses are pinned for TLS connections.

Queries use fromUtc/toUtc, pageSize and continuationToken; logs additionally accept source, level and traceId. Default range is the last 24 hours; maximum 168 hours and 100 rows. Continuations freeze range/filters/page size and expiry. Portal IDs/cursors are authenticated encrypted tokens bound to organization, user, application, operation, environment, origin, credential version and application version. They cannot be reused across app switches, users, operations or rotations. Target opaque tokens are limited to 8192 characters; portal tokens to 16384. Safe target DTOs are projected again; unknown/raw fields, arbitrary summaries and correlation values are suppressed. Configurable raw fields use the verified target mode under IBM-0097; no request can toggle it.

Failures return safe envelopes: unauthorized/unassigned access 401/403, invalid portal query/token 400, missing record 404, bounded timeout 504, target throttling 429, invalid successful envelopes 502, unavailable transport/invalid JSON/provider failures 503. Raw response bodies, headers, keys and SDK exceptions are never passed through. Caller cancellation propagates. Modules are optional; absent registration leaves these routes unavailable. No portal UI or deferred consumer host is required.

## Authorized app selector (IBM-0092)

AddIBeamManagementApplicationSelectorApi(configuration) opts into portal diagnostics and changes GET /api/management/applications to the authorized selector. Each item contains only applicationId, applicationKey, name, environment and independently granted errors/logs permissions. Registry administration remains GET /api/management/applications/registry; item administration routes retain their administrator guard. Without selector registration, the original administrator list behavior remains available.

IManagementApplicationSelector coordinates entity service lookup ports, live users, live assignments and verified connection capability metadata. It never reads a secret or calls a diagnostic target. Disabled/disconnected/unassigned apps are omitted, with no administrator bypass. Bounded pagination scans at most five catalog pages per request; an empty page may have a continuation. Selector tokens bind org/user/page size/expiry with the portal protection key. Selectors recheck live state before returning; diagnostic routes always authorize independently. A single support bearer session can switch app IDs without a customer-app sign-in. The existing /me endpoint identifies that support session.

## Management audit (IBM-0093)

AddIBeamManagementAudit(configuration) opts Management service assemblies into a dedicated IServiceOperationExecutor audit pipeline. Supply IManagementAuditRepository and trusted IManagementPrincipalProvider. Existing customer-service executor, audit sinks, authorizers and configuration continue to handle other assemblies. AddIBeamManagementAzureTableAuditStore(dedicatedTableClient) supplies an append-only adapter; provision its dedicated table through deployment tooling, never a runtime read.

ManagementAuditEntity contains generated audit ID, UTC time, expiry, org/user or machine credential ID, app/subject IDs where available, allowlisted operation, success flag and generated correlation. The sink discards generic transaction snapshots, errors, header/request context and arbitrary action text. Invitation/bootstrap calls without a principal retain their service-configured organization; an unauthenticated caller is recorded with unknown subject, never an asserted human actor. Successful target reads identify the authenticated machine credential and configured deployment. No support audit list, mutation or delete endpoint is exposed.

IBeam:Management:Audit:RetentionDays defaults to 90 (1–3650); each append records ExpiresUtc. Azure Table does not automatically enforce TTL: deployment-owned maintenance, using a separate deletion credential, must remove expired rows or apply archive/immutability requirements. Support runtime credentials should allow append only and have no retention/deletion role. WriteTimeoutSeconds defaults to 5 (1–30). FailOnWriteError defaults to true: a safe failure prevents a successful response. Audit is not transactionally coupled to another table: a write may have committed before an audit failure; read current state before retrying a mutation. The shared executor can attempt a success audit and a failure audit once; the sink calls no business service/logger, preventing recursive diagnostic writes. Explicit false permits best-effort support operations; monitor the management.audit.write_failures counter from IBeam.Management.Audit. Cancellation propagates.

The scoped IManagementAuditContext generates a fresh correlation ID in the portal. The backend client sends only that ID in X-IBeam-Management-Correlation, never browser actor headers. Target resource filtering accepts a single 32-character lowercase hexadecimal correlation only after machine authentication; duplicates/malformed values are replaced by the target-generated ID. This links events across processes; it is not proof of a human identity. Credential authentication events happen before the target resource filter and have their own generated request correlation. Diagnostic resource events share the accepted portal correlation. Browser-supplied correlation is never adopted by the portal.

Deployment configuration, identity storage choices, MFA/bootstrap, onboarding, exact registrations/endpoints, network/proxy policy, secret rotation, audit retention/failure behavior and the deferred consumer host are covered by the [Management deployment handoff](https://github.com/MedPACTech/IBeam/blob/development/docs/management/deployment-and-handoff.md). [Source/history isolation](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-source-isolation.md) describes which historical rows may be exposed. Generated-package validation uses the package-only two-target smoke sample; IBM-0097 supplies independent host-owned errors/logs sanitization switches, safe by default and restricted to explicit non-Production contexts. See [sanitization settings](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-sanitization.md).

## Configurable diagnostic text

IBeam:Management:Diagnostics:Errors:Sanitization:Enabled and Logs:Sanitization:Enabled default to true, including blank values. Only false enables the typed unsanitized response object. Both module startup guards, readers and projections require an explicit non-Production host/deployment. /info advertises independent unsanitizedCapabilities; portal reads preserve only the verified operation's marked raw object. Mode changes invalidate target and portal links/cursors. Source/category/grant/budget restrictions and metadata-only audit are unchanged. Details, allowed environment names, repository ports and integration examples are in the [sanitization guide](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-sanitization.md).
## Customer directory forwarding

`AddIBeamManagementPortalCustomerDirectories(configuration)` registers typed tenant/user/invitation directory services with current support grants, scoped signed HTTPS calls and encrypted pagination. Supply a delegated actor provider and a separate portal cursor key. See [customer directory API](../docs/management/customer-identity-api.md).

Enterprise onboarding HTTP and portal registration, safe business status, scoped operation polling and explicit retry: [API contract](../docs/management/enterprise-onboarding-api.md).

Approved tenant roles and invitation-only customer provisioning: [registration and API contract](../docs/management/customer-provisioning.md). Catalog visibility uses the independent roles.read grant; no account/membership is created before customer verification.

IBM-0111 selected-app role catalog, customer provisioning and invitation lifecycle forwarding plus read-only receipt status: [portal API contract](../docs/management/portal-customer-provisioning.md).

IBM-0112 versioned tenant membership/role administration, concurrent final-administrator protection and required customer access enforcement: [lifecycle contract](../docs/management/customer-membership-lifecycle.md).

IBM-0113 conditional tenant activation/deactivation, lifecycle recovery and current customer access rejection: [status contract](../docs/management/customer-tenant-status.md).

Onboarding policy discovery and optional durable consumer initialization: [integration contract](../docs/management/customer-onboarding-policy.md).

Optional trusted tenant provenance and existing signup compatibility: [integration contract](../docs/management/tenant-provenance.md).

IBM-0117: an app assignment can contain both diagnostic permissions and every explicitly assignable customer operation (16 total currently). Bound the size from the known catalog; keep distinct known values, live assignment checks and independent delegation ceilings. Unknown/wildcard authority stays rejected.

IBM-0124 adds explicit scoped ownership lookup and transfer with independent grants and signed exact versions. See [customer ownership APIs](../docs/management/customer-ownership-api.md).

Registered operation transport: AddIBeamManagementRegisteredOperationsPortal / Target and typed IManagementOperationClient are opt-in. See docs/management/registered-operation-dispatch.md for required providers, explicit permission migration and recovery.

IBM-0137 adds opt-in typed purge preflight, exact confirmation and read-only progress. Portal hosts use `AddIBeamManagementPortalTenantPurgeApi(configuration)`; no native worker/provider is installed. See [tenant purge](../docs/management/tenant-purge.md) for routes, independent grants, unknown-reply recovery and metadata-only audit.
