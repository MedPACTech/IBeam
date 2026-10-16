# IBeam.Management.Diagnostics.Api

Optional target diagnostics API library for one application/environment. It remains independent of IBeam Identity and the separate portal host.

For persisted management credentials, supply the repository and register the opt-in adapter:
```csharp
builder.Services.AddIBeamManagementAzureTableCredentialStore(builder.Configuration);
builder.Services.AddIBeamManagementDiagnosticsCredentialApi(builder.Configuration);
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```
The first extension is in IBeam.Management.Repositories.AzureTable; the second is in IBeam.Management.Diagnostics.Api.Authentication. Supply a dedicated TableClient instead of configuration when using managed identity/table-scoped SAS. Runtime readers need read permission only; trusted tooling provisions the table and issues keys separately.

Configure IBeam:Management:Diagnostics:ApplicationKey and Environment with the exact local deployment identity. Credential storage uses independently configured IBeam:Management:Credentials:Storage:ConnectionString and TableName (default ManagementApiCredentials), never customer/Identity storage fallback. Keep credentials in a secret store. Nothing changes or renames IBeam.Admin.API.

The named IBeam.Management.Credential scheme accepts exactly one X-IBeam-Management-Key header, only on /api/management-diagnostics routes. It rejects Authorization plus key combinations and does not read keys from Bearer, query strings or bodies. Customer/default authentication settings stay unchanged, and the management handler refuses ordinary customer routes even when ASP.NET automatically selects a sole scheme as its default. Customer JWTs, portal human tokens and legacy tenant API keys cannot substitute.

Each request validates the key hash against its current record, expiry/status, explicit grants and exact app/environment. The principal contains credential identity, deployment, permissions and an opaque record version, with no secret/hash. IManagementDiagnosticsAccessService.RequireAsync repeats live record/version/grant checks before a query. Future error/log readers must invoke it inside their service operation before repository access. Rotation/revocation/permission changes invalidate stale principals; already authorized in-flight work may finish. Storage failures grant no access; configure the host's standard safe global error middleware for infrastructure errors.

GET /api/management-diagnostics/info requires management.diagnostics.info and returns application/environment, protocol version and only implemented capabilities. Base registration advertises only info. The optional error and log HTTP modules are described below. Merely granting diagnostic permissions installs no endpoints. management.diagnostics.errors.read and management.diagnostics.logs.read are independent and have no wildcard or owner/admin bypass. Missing/invalid authentication returns 401; a valid credential lacking an operation returns safe 403.

Optional network configuration: IBeam:Management:Credentials:Network:AllowedNetworks is an array of up to 100 CIDRs, such as 10.20.0.0/16 or 2001:db8::/32. Empty means no additional IP restriction; configured ranges deny unknown addresses. IPv4-mapped addresses normalize to IPv4. The policy reads HttpContext.Connection.RemoteIpAddress and never consumes a raw forwarded header. A reverse-proxy host must explicitly configure trusted proxies/networks and run its forwarded-header middleware before authentication. This library does not enable arbitrary forwarded-header trust.

Operations execute through IServiceOperationExecutor. Credential ID, operation, success/failure and server correlation are available to the audit sink without key/hash snapshots. When no audit providers are already registered, this module supplies a safe HTTP actor/request context provider; existing host providers are retained and must supply equivalent safe context. AddIBeamManagementAudit(configuration) with a dedicated IManagementAuditRepository provides optional durable metadata-only management audit and isolated failure/retention policy. Configure that sink/policy before production and redact X-IBeam-Management-Key from ingress/HTTP logging; never log requests or keys. No issue/rotate/revoke HTTP routes are exposed.

Bring-your-own authentication remains supported: register the host's trusted machine scheme and call AddIBeamManagementDiagnosticsApi(configuration, o => o.AuthenticationScheme = "YourManagementScheme") instead of the persisted-key adapter. That scheme must verify its own secret/status/deployment and issue explicit management context. The general API validates required scheme/deployment configuration and its services retain their original context guards. Do not register both API paths in one host.

## Optional system-error endpoints (IBM-0084)

Install the read repository and opt-in error module alongside the existing target authentication:
```csharp
services.AddIBeamManagementAzureTableCredentialStore(configuration);
services.AddIBeamManagementDiagnosticsCredentialApi(configuration);
services.AddIBeamManagementAzureTableSystemErrorReader(configuration);
services.AddIBeamManagementSystemErrorsApi(configuration);
```
The error API extension is in IBeam.Management.Diagnostics.Api.Controllers; repository extensions are in IBeam.Management.Repositories.AzureTable.Diagnostics (and the credential repository's root namespace). It installs the bounded internal reader plus safe SystemErrorReaderService. Do not also duplicate their lower-level registrations. Existing IBeam:Management:Diagnostics:Errors source-isolation, storage, key and budget settings remain required. The module validates its service/repository dependencies at startup.

GET /api/management-diagnostics/system-errors accepts fromUtc, toUtc, pageSize and continuationToken. It returns the standard IBeam cursor envelope: data, pageSize, continuationToken, success and request traceId. Continue even when a page is empty if continuationToken is present; omitted query bounds/page size retain the original protected query on continuation. GET /api/management-diagnostics/system-errors/{errorId} uses the query scope encrypted into the opaque ID.

Both routes require Errors.Read with the exact live credential/deployment/version checks, including a fresh check immediately before each storage read. Public DTOs contain only id, occurredAtUtc, source, summary and correlationId:
- Exact stored source RequestPipeline maps to a fixed RequestPipeline label and fixed request-processing summary.
- Every other source maps to fixed System/generic-summary text; untrusted source strings are never copied.
- Record correlationId is a keyed, deployment-scoped c1_ pseudonym derived from a bounded stored trace ID, not the raw trace. Blank/oversized trace IDs produce null. It is stable with the same key/app/environment and changes on key rotation. The envelope traceId remains the server's current request identifier.
- No exception/message/path/method, customer content, stored trace ID, storage key or provider continuation is serialized. The underlying adapter does not select raw exception/request fields.

Application results use Cache-Control: no-store. Expected failures use safe IBeam envelopes: 400 for invalid queries/continuations (including model-binding failures without attempted-value echoes), 403 for insufficient management permission, 404 uniformly for missing/invalid/expired/foreign detail IDs, 503 for unavailable reads, and 504 for a reader deadline. Actual client cancellation stays cancellation. Provider error text/inner exceptions are sanitized before operation audit and response handling, including when the host enables detailed errors. Keep normal ingress/header redaction and durable audit configuration.

Only GET routes are installed; there are no error create/update/delete routes. The controller is explicitly added by the optional module and is excluded from base assembly discovery. Base target API installations keep error routes absent and advertise only their installed capabilities.

Installing the error HTTP module adds management.diagnostics.errors.read to /info through IManagementDiagnosticsCapability. This advertises an implemented endpoint, not a credential grant; info and error permissions remain independent. Parallel diagnostics modules should add their own capability implementation with TryAddEnumerable instead of replacing the info service, so capabilities compose. Existing three/four-argument info-service constructors remain available. The IBeam.Admin.API host remains separate work.

## Optional system-log endpoints (IBM-0085, IBM-0086)

Install the read repository and opt-in log module alongside the existing target authentication. It is independent of the error module; install either or both:
```csharp
services.AddIBeamManagementAzureTableSystemLogReader(configuration);
services.AddIBeamManagementSystemLogsApi(configuration);
```
Configuration lives under IBeam:Management:Diagnostics:Logs with its own SourceMode/confirmation, budgets and 32-byte ProtectionKey (never reuse the error key), and Logs:Storage:ConnectionString/TableName naming the existing SystemLogs table explicitly. Optional Logs:AllowedSources lists extra Source values this deployment writes that are safe to show and filter verbatim (pattern ^[A-Za-z][A-Za-z0-9._-]{0,127}$, at most 32, never "System").

GET /api/management-diagnostics/system-logs accepts fromUtc, toUtc, pageSize, source, level, traceId and continuationToken, and returns the same cursor envelope as errors. source must be a built-in IBeam source (RequestPipeline, AppDomain.UnhandledException, TaskScheduler.UnobservedTaskException) or an AllowedSources entry; level is one of Trace, Debug, Information, Warning, Error, Critical (exact case); traceId is an exact raw trace identifier such as the traceId a customer received in an API error envelope (^[A-Za-z0-9:._|-]{1,128}$). Filters are frozen into the encrypted cursor: on continuation they may be omitted or restated, never changed. GET /api/management-diagnostics/system-logs/{logId} uses the query scope encrypted into the opaque ID. Error IDs/cursors are not accepted here, and log IDs are not accepted by the error routes.

Only operational rows are read: exact SYSTEM|DAY|yyyyMMdd partitions with Category System. Tenant partitions and EntityChange/SelectRollup audit rows, including tenant-less audit rows that share the SYSTEM partition, are never returned. Public DTOs contain only id, occurredAtUtc, level, source, summary and correlationId. Level is normalized to the known set or Unknown; source/summary come from the fixed catalog (unapproved sources become System with a generic summary); correlationId is a log-key c1_ pseudonym. Message, Detail, actor/device/IP/user agent, tenant/entity identifiers and before/after/original/transformed JSON are never selected from storage. Logs have no tenant attribution, so access is application/environment-wide; no per-tenant restriction is claimed.

Both routes require management.diagnostics.logs.read with the same live credential/deployment/version checks before every storage read. Logs.Read does not imply Errors.Read and vice versa. Responses, status codes, sanitization and no-store behaviour match the error routes. Only GET routes are installed, and the module adds management.diagnostics.logs.read to /info additively.

Deployment configuration, identity storage choices, MFA/bootstrap, onboarding, exact registrations/endpoints, network/proxy policy, secret rotation, audit retention/failure behavior and the deferred consumer host are covered by the [Management deployment handoff](https://github.com/MedPACTech/IBeam/blob/development/docs/management/deployment-and-handoff.md). [Source/history isolation](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-source-isolation.md) describes which historical rows may be exposed. Generated-package validation uses the package-only two-target smoke sample; IBM-0097 configurable scrubbing remains deferred.
