# IBeam.Management.Services

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

## Application administration

AddIBeamManagementApplications registers IManagementApplicationService. All methods enforce the human management context and management.applications.admin before storage access, execute through IServiceOperationExecutor and sanitize unexpected provider failures before audit. No customer role implies this permission. Register a host-owned IManagementPrincipalProvider and isolated IManagementApplicationRepository; registration never grants diagnostic access.

POST creates enabled metadata; PATCH supplies complete mutable name/origin/enabled configuration and an expected version. Application key/environment remain immutable. Approved outbound destinations and connection secrets belong to the separate connection module.
