# Management API

Date: 2026-10-15

Epic estimate: **107 points** across 10 features and 20 cards. Cards use Fibonacci estimates; feature totals are their sums. Hubbsly stores points on cards, with feature totals recorded in feature descriptions.

Deliver reusable IBeam NuGet packages for a central support portal API: one isolated MFA support login, app/environment registration and secret-backed connections, support-user assignments to one or many apps, and read-only sanitized system errors/logs through deployment-scoped management credentials. Preserve API -> Services -> Repository and consumer/customer data isolation. Support separate identity storage or separate prefixed tables in shared storage. Defer customer provisioning, impersonation, frontend work, production publishing/deployment and changes/rename to IBeam.Admin.API, the future NuGet-consuming test host.

Framework work stays in IBeam. IBeam.Admin.API remains the deferred, separately named runnable host that will consume generated NuGet packages; no host edits, frontend work or production deployment are included.

Architecture: portal -> Management API -> entity-focused services -> repositories / registered diagnostics HTTP client; target Diagnostics API -> diagnostic services -> read-only diagnostic repositories. Core contracts remain independent of Identity and HTTP. All custom service methods use IServiceOperationExecutor.

Storage: management identity may use a separate connection or a distinct prefixed identity table set in shared storage. New tables: ManagementApplications, ManagementApplicationCredentials (secret references), ManagementApplicationAccess, ManagementAudit; each target has ManagementApiCredentials (secret hashes). Each app/environment is a separate registration. Portal identity and target diagnostics connections remain independently configurable.

Existing SystemErrors has no tenant/application attribution and SystemLogs includes business audit payloads. Initially require isolated deployment sources, only approved system categories and safe allowlisted projections; ambiguous shared-source historical records fail closed. Per-customer-tenant restriction is deferred until trustworthy attribution exists.

## MGT-F01: Management package foundation and protected API boundaries

Estimate: **5 points**, summed from the cards below.

Introduce reusable .NET 10 management contracts, services, portal API and target diagnostics API. Expose authenticated portal identity and target capability endpoints through thin controllers and IBeam service operations; remain independent of the deferred IBeam.Admin.API host.

Dependencies: .

### MGT-001 / IBM-0078: Bootstrap Management packages and protected identity/capability endpoints

Estimate: **5 points**.

Create IBeam.Management, IBeam.Management.Services, IBeam.Management.Api, IBeam.Management.Diagnostics.Api and focused tests. Add explicit host-provided authentication-scheme options; /api/management/me and /api/management-diagnostics/info; immutable response contracts; deny-by-default service checks. Do not expose error/log reads or pretend database adapters are implemented.

Touches: IBeam.Management/, IBeam.Management.Services/, IBeam.Management.Api/, IBeam.Management.Diagnostics.Api/, IBeam.Tests.Management/, IBeam.sln.

- [x] All four projects build and produce separate NuGet artifacts with README and agent guidance; core contracts do not reference Identity, ASP.NET Core, or repositories.
- [x] Portal identity requires the configured support authentication scheme, authenticated named human, support organization and explicit management context; customer/default identities cannot satisfy it.
- [x] Target info requires its configured machine scheme, explicit management credential context and the configured application/environment binding; portal human tokens cannot satisfy it.
- [x] Controllers call service methods; services invoke IServiceOperationExecutor; service checks still reject unauthorized direct calls.
- [x] Missing/blank authentication or deployment configuration fails validation; services have no allow-all authorization default.
- [x] HTTP tests prove 401 for missing/wrong schemes, 403 for wrong subject/binding, and 200 for the intended identity; registration never replaces consumer authentication defaults.
- [x] IBeam.Admin.API is not modified or renamed; solution and package registration examples document this foundation's limited scope.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

## MGT-F02: Isolated support identity and session lifecycle

Estimate: **13 points**, summed from the cards below.

Reuse IBeam.Identity for one named, MFA-protected support login. Allow a separate storage connection or an isolated prefixed identity table set on shared storage; support organization membership is independent of customer tenant membership.

Dependencies: MGT-F01.

### MGT-002 / IBM-0079: Configure isolated management identity storage and bootstrap

Estimate: **5 points**.

Support Management identity connection/prefix mapping into existing Identity provider. Reuse AspNetUsers/AspNetIndex/AspNetRoles, Tenants, TenantUsers, UserTenants, Roles, TenantInvites, OtpChallenges, AuthIdentifiers, AuthSessions and AuthAttempts plus provider-required tables. Seed the initial organization/administrator through trusted setup.

Touches: IBeam.Management.Identity.Services/, IBeam.Identity.Repositories.AzureTable/Extensions/, IBeam.Tests.Management.Identity/, IBeam.sln.

- [x] Separate-store and shared-store/prefix modes are verified without modifying customer identity records.
- [x] Same email in customer and support stores does not transfer memberships or roles.
- [x] Provider-required indexes, sessions, OTP/recovery and schema metadata remain isolated, not only the users table.
- [x] Bootstrap is explicit, idempotent and unavailable through public self-registration; secret values are absent from logs/config examples.
- [x] Configured storage identities enforce the intended isolation; a naming prefix alone is not described as a permission boundary.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

### MGT-003 / IBM-0080: Enforce support sign-in, MFA, refresh and disable semantics

Estimate: **8 points**.

Reuse selected /api/auth sign-in/MFA/refresh flows in the portal host with management issuer/audience, support context and membership validation. Provide support account disable and session invalidation.

Touches: IBeam.Management.Identity.Services/, IBeam.Management.Identity.Api/, IBeam.Identity.Services/Auth/PasswordAuthService.cs, IBeam.Tests.Management.Identity/, IBeam.sln.

- [x] Support registration is invitation-only; an authenticated customer cannot enroll themselves.
- [x] Management tokens carry a distinct accepted audience and cannot authenticate customer data endpoints; ordinary tokens fail management endpoints.
- [x] MFA completion is required before management privileges become usable; incomplete/pre-tenant tokens fail.
- [x] Disabled users/organizations and revoked sessions are rejected on subsequent protected requests with a documented bounded revocation policy.
- [x] Refresh cannot restore removed memberships or permissions; grants are not trusted indefinitely from stale claims.
- [x] Tests cover recovery/invitation abuse, token substitution, organization mismatch, expiration and disabled users.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

Verified: IBM-0079 and IBM-0080 implement the optional Identity services/API adapter. 53 focused tests, 216 existing Identity tests and 34 Management foundation tests passed. Runtime signup/recovery remain closed; validated invitations are MGT-012. Per-request storage checks reject subsequent requests after membership/role/session changes; requests already authorized may finish. Shared-account prefixes provide logical isolation only. Cloud credential/RBAC setup remains an operator responsibility.

## MGT-F03: Deployment-scoped management credentials

Estimate: **10 points**, summed from the cards below.

Each application/environment issues its own revocable machine credential with explicit diagnostic capabilities. Store only secret hashes in ManagementApiCredentials and reject those credentials on ordinary application endpoints.

Dependencies: MGT-F01.

### MGT-004 / IBM-0081: Persist and provision target management credentials

Estimate: **5 points**.

Add ManagementApiCredentialEntity and entity-focused Azure Table repository, trusted issue/rotate/revoke setup operations, and explicit deployment binding. Reuse safe cryptographic techniques without turning an existing tenant-admin key into an application-wide key.

Touches: IBeam.Management/Entities/, IBeam.Management.Services/Credentials/, IBeam.Management.Repositories.AzureTable/.

- [x] Credential records include ID, secret hash, deployment binding, explicit capabilities, status, expiry and revocation metadata; raw secrets are returned only on issuance.
- [x] Credential creation/rotation is restricted to trusted target operators and is not a public portal-admin endpoint.
- [x] Constant-time secret verification, high-entropy issuance, revocation and expiry are tested; hashes and raw keys never enter audit snapshots.
- [x] Separate application/environment credentials cannot be substituted; legacy tenant credentials are rejected by this management credential type.
- [x] The repository project remains independent of services/APIs and runtime readers need no schema-creation privileges.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

Verified IBM-0081: 19 credential tests (including a real Azure Table lifecycle/ETag fixture) and 34 management foundation tests passed. Provisioning is trusted tooling only; runtime validation is read-only and no schema is created. Five points retained.

### MGT-005 / IBM-0082: Authenticate target management keys with bounded capabilities

Estimate: **5 points**.

Implement dedicated target authentication scheme and management context, /info integration, operation checks and optional network restrictions; do not alter default customer auth.

Touches: IBeam.Management.Diagnostics.Api/Authentication/, IBeam.Management.Services/Credentials/.

- [x] Missing/malformed/unknown/revoked/expired keys fail; customer JWT/API keys and support human tokens fail target management authentication.
- [x] Errors.Read and Logs.Read are independent; no implicit wildcard or owner/admin bypass exists.
- [x] Every target query validates credential and deployment; cross-deployment access is denied.
- [x] Optional IP controls evaluate trustworthy server/proxy-derived addresses, never an arbitrary client header.
- [x] Credential ID, operation and correlation are auditable without storing the raw credential; existing customer endpoint tests stay valid.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

Verified IBM-0082: 61 credential/HTTP/provider tests, 34 management foundation tests and 60 existing Identity API tests passed. Named scheme and reserved route scope preserve customer authentication; current deployment/version/grants are reread before operations. CIDR controls use server-observed addresses, never raw forwarded headers. Five points retained; feature total is 10 points. Error/log reader endpoints remain MGT-F04/F05.

## MGT-F04: Read sanitized system errors

Estimate: **10 points**, summed from the cards below.

Read existing SystemErrors through read-only repository adapters and safe DTOs. Expose bounded list/detail target endpoints, with application isolation and protected opaque record IDs/cursors; suppress unsafe legacy content.

Dependencies: MGT-F03.

### MGT-006 / IBM-0083: Read existing error partitions with protected identifiers and bounded queries

Estimate: **5 points**.

Implement SystemErrorEntity read adapter for ERR|yyyyMMdd partitions and current row keys, DiagnosticQuery and cursor codec. Keep diagnostics storage separately configurable from management identity storage.

Touches: IBeam.Management.Repositories.AzureTable/Diagnostics/, IBeam.Management/Models/, IBeam.Management.Services/Diagnostics/, IBeam.Tests.Management.Diagnostics/, IBeam.sln.

- [x] Existing error rows can be read without rewriting them or copying them to the portal store.
- [x] Default query is bounded (24h); configurable maximum window/page/scan budget prevents unbounded queries and cancellation is honored.
- [x] Continuation tokens and record IDs are authenticated and bound to deployment, record type and query; tampering/replay against another context fails.
- [x] Diagnostic connection/table options never implicitly select the portal's prefixed identity diagnostic tables.
- [x] Shared/mixed application sources require trustworthy deployment tagging; ambiguous/untagged rows are not exposed.
- [x] Azurite/integration fixtures cover date boundaries, empty pages, stable pagination and missing records without full-table scans.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

Verified IBM-0083: 34 diagnostics tests (including three real-provider fixtures), 61 credential tests and 34 management foundation tests passed. Reader registration is explicit and adds no HTTP routes/capabilities. Legacy rows require a confirmed dedicated source; shared rows require matching trusted tags. Metadata-only reads preserve existing rows; row/request/deadline budgets and encrypted source/deployment/query-bound identifiers are enforced. Five points retained; IBM-0084 remains the public projection/endpoints card.

### MGT-007 / IBM-0084: Expose safe error list/detail service and target endpoints

Estimate: **5 points**.

Add SystemErrorReaderService and GET /api/management-diagnostics/system-errors and /{errorId}. Return allowlisted safe fields; no raw exception or request payload exposure.

Touches: IBeam.Management.Services/Diagnostics/SystemErrorReaderService.cs, IBeam.Management.Diagnostics.Api/Controllers/.

- [x] Both routes require Errors.Read and enforce the deployment before repository access.
- [x] Thin controllers delegate to services routed through IServiceOperationExecutor with stable operation/audit names.
- [x] Safe error DTOs include opaque ID, timestamp, approved source/summary and safe correlation metadata; unknown free text is suppressed.
- [x] Raw exception text, query strings, customer payloads, passwords/tokens and storage keys are never serialized, including error responses.
- [x] Unauthorized detail IDs do not reveal another deployment's existence; validation/not-found/forbidden responses follow IBeam conventions.
- [x] Tests prove safe legacy behavior, insufficient permissions, cancellation and read-only operation; no delete/update diagnostic endpoints exist.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

Verified IBM-0084: 68 diagnostics tests, 61 credential tests and 34 management foundation tests passed. Opt-in GET list/detail routes use fixed approved source/summary strings, opaque deployment-scoped correlation, generic validation/not-found/failure envelopes, live Errors.Read and the bounded internal reader. Provider messages are sanitized before audit; query/model-binding failures never echo inputs. Read-only SAS API fixtures retain unchanged rows. Only installed HTTP modules add capabilities through IManagementDiagnosticsCapability, allowing the separate logs feature to compose. Five points retained; MGT-F04 is complete at 10 points. The logs feature belongs to the other session.

## MGT-F05: Read sanitized system logs

Estimate: **11 points**, summed from the cards below.

Read approved operational SystemLogs from the existing logging provider while excluding business audit payloads and unsafe free text. Support bounded source/level/trace filtering and list/detail endpoints.

Dependencies: MGT-F03, MGT-F04.

### MGT-008 / IBM-0085: Implement operational log reader and projection policy

Estimate: **5 points**.

Add AzureTableSystemLogReadRepository and SystemLogReaderService using existing SYSTEM|DAY|yyyyMMdd records. Do not expose EntityChange/SelectRollup or before/after/original/transformed JSON.

Touches: IBeam.Management.Repositories.AzureTable/Diagnostics/, IBeam.Management.Services/Diagnostics/SystemLogReaderService.cs.

- [x] The reader queries approved system categories/partitions only and excludes tenant business audit records by default.
- [x] BeforeJson, AfterJson, OriginalJson, TransformedJson, actor/device/IP metadata and unsafe Detail/Message are absent from outbound DTOs.
- [x] Safe source/level/time/trace filters, authenticated cursors, row IDs, page/scan budgets and cancellation match the error contract.
- [x] Sensitive canary fixtures in every excluded field never appear in responses or management audit.
- [x] Logs.Read does not imply Errors.Read and no tenant-restricted claim is made when source attribution cannot support it.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

Verified IBM-0085: 130 diagnostics tests passed (68 existing plus 62 new), including seven private Azurite cases. 34 management foundation and 59 credential (2 opt-in skipped) regressions passed. Reader queries exact SYSTEM|DAY partitions with Category System; tenant partitions, EntityChange/SelectRollup rows (including tenant-less audit in the SYSTEM partition), rollup keys and malformed rows are never returned. $select excludes Message/Detail, actor/device/IP/user agent, tenant/entity and every *Json column; canaries seeded in each are absent from responses, audit and service logs. Source/level/trace filters are validated, pushed down, rechecked per row and frozen into AES-GCM cursors bound to system-logs/app/environment/source binding; error and log tokens are not interchangeable. Live Logs.Read is checked before every provider read and does not imply Errors.Read. Logs carry no tenant attribution, so no per-tenant restriction is claimed. Mutation check: selecting Message and dropping the Category guard fails the canary test. Configurable sanitization is IBM-0097.

### MGT-009 / IBM-0086: Expose target log list/detail endpoints

Estimate: **3 points**.

GET /api/management-diagnostics/system-logs and /{logId}; IBeam response helpers, service permissions/audit and deployment-bound detail lookup.

Touches: IBeam.Management.Diagnostics.Api/Controllers/, IBeam.Tests.Management/.

- [x] List/detail require Logs.Read and the correct machine credential/deployment binding.
- [x] Response shapes use safe log DTOs and existing response conventions; storage entities are never returned.
- [x] HTTP tests cover missing/wrong credentials, cross-type/cross-deployment IDs, invalid ranges/cursors and missing rows.
- [x] Reader services remain callable without HTTP while enforcing trusted service access context and operation execution.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

Verified IBM-0086: Opt-in GET /api/management-diagnostics/system-logs and /{logId} via AddIBeamManagementSystemLogsApi; Logs.Read capability composes with errors in /info. HTTP tests cover anonymous/customer/human/legacy/revoked/expired/other-deployment/info-only/errors-only denial with zero reads, cross-type error IDs/cursors, invalid ranges/filters/cursors without echo, uniform 404s, sanitized 503, 504 deadline, no write routes, direct service calls without HTTP context, missing-repository startup failure and a read-only SAS end-to-end run. The shared diagnostics filter was renamed ManagementDiagnosticApiFilter (internal).

### MGT-020 / IBM-0097: Configurable sanitization for errors and logs

Estimate: **3 points**. Hubbsly card: 6684ca3b-1e8b-48b1-8d16-e1104896eede.

Independent Errors/Logs Sanitization.Enabled settings default to true, including missing/blank values. Explicit false adds a typed unsanitized object only in supported non-Production host/deployment contexts. Target info advertises each raw module; the current portal preserves only verified raw fields and binds links/cursors to the mode. Source/category/grant/budget restrictions, read-only storage and metadata-only audit remain enforced. See docs/management/diagnostic-sanitization.md and the card for verified implementation evidence.
## MGT-F06: Application registry and secret-backed connections

Estimate: **13 points**, summed from the cards below.

Register Qurvia, Bindry or MedInsights per environment, store backend-only secret references and test registered target connections. Separate metadata from credential material and limit outbound destinations.

Dependencies: MGT-F02, MGT-F03.

### MGT-010 / IBM-0087: Persist application registry and configuration endpoints

Estimate: **5 points**.

ManagementApplicationEntity/ManagementApplications, entity-focused repository and service; GET/POST /api/management/applications and GET/PATCH /{applicationId}. Scope by management organization; one record per app/environment.

Touches: IBeam.Management/Entities/ManagementApplicationEntity.cs, IBeam.Management.Repositories.AzureTable/, IBeam.Management.Services/Applications/, IBeam.Management.Api/Controllers/.

- [ ] App/environment uniqueness is enforced within the support organization; metadata has ID/key/name/environment/URL/status/timestamps and concurrency token.
- [ ] Server-resolved organization scope is mandatory for lists and item reads/updates; a client cannot choose another support organization.
- [ ] Only explicit application-administration permission can create/update/disable; diagnostic readers cannot.
- [ ] Application creation does not automatically grant diagnostic access or create customer-tenant membership.
- [ ] Duplicate/conflict/not-found/validation handling and auditable changes follow IBeam conventions.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

### MGT-011 / IBM-0088: Store connection secrets and verify registered target identities

Estimate: **8 points**.

ManagementApplicationCredentialEntity/ManagementApplicationCredentials, IManagementSecretStore with environment-reference and secret-store adapters; PUT /applications/{id}/connection and POST /connection/test.

Touches: IBeam.Management.Services/Applications/ManagementApplicationConnectionService.cs, IBeam.Management.Services/Secrets/, IBeam.Management.Api/Controllers/.

- [ ] Submitted keys are write-only and stored in a secret store; app/credential rows contain references and metadata only.
- [ ] Environment-reference mode supports fixed deployments; portal submissions require a writable secret-store adapter and never edit process environment variables.
- [ ] Only approved HTTPS destinations are callable; redirect credential forwarding, arbitrary URL testing, DNS/private-network bypasses and user-controlled host substitutions are denied by policy.
- [ ] Connection test confirms target deployment/environment and supported capabilities through /info without returning secrets.
- [ ] Rotation validates a pending credential before activation, preserves the working connection on failure and records audit without request-body/secret capture.
- [ ] List/detail/update errors, logs, traces and audit cannot disclose key material; timeout/cancellation and target outage behavior are tested.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

## MGT-F07: Support users and per-application access

Estimate: **13 points**, summed from the cards below.

Invite and disable named support users, assign one person to one or many registered apps, and independently grant error/log reading. Every operation enforces support-organization scope and current assignments.

Dependencies: MGT-F02, MGT-F06.

### MGT-012 / IBM-0089: Expose controlled support directory and invitation lifecycle

Estimate: **5 points**.

ManagementSupportUserService wrapping existing Identity directory/invite services; GET /support-users, POST /support-invitations, POST /support-invitations/accept and PATCH /support-users/{userId}.

Touches: IBeam.Management.Services/Users/, IBeam.Management.Api/Controllers/.

- [ ] Invite/list/disable operations require support-user administration permission and matching management organization.
- [ ] Invitation acceptance is one-time, expires, verifies intended identity and cannot change organization or elevate roles from submitted fields.
- [ ] Support users are stored in management Identity tables, not duplicated in a new profile/auth table.
- [ ] Disable prevents new diagnostic access and invalidates support sessions; administrative recovery rules prevent accidental loss of the final administrator.
- [ ] No customer-user provisioning, impersonation or customer credential editing is added.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

### MGT-013 / IBM-0090: Persist app assignments and enforce access/delegation boundaries

Estimate: **8 points**.

ManagementApplicationAccessEntity/ManagementApplicationAccess plus repository and service; GET/PUT/DELETE /applications/{id}/support-users[/{userId}]. Initial grants are management.diagnostics.errors.read and management.diagnostics.logs.read.

Touches: IBeam.Management/Entities/ManagementApplicationAccessEntity.cs, IBeam.Management.Services/Access/, IBeam.Management.Repositories.AzureTable/, IBeam.Management.Api/Controllers/.

- [ ] A person can receive zero/one/many app assignments and independent Errors.Read/Logs.Read; app and user must belong to the same management organization.
- [ ] Only trusted access administrators can assign/revoke within their explicit delegation ceiling; readers cannot assign themselves or grant unknown/wildcard permissions.
- [ ] Missing/disabled/revoked application, user, organization or assignment denies access; no global customer-owner/admin role bypass exists.
- [ ] Assignments are rechecked on each read and revocation is effective without token refresh; cache invalidation is tested if caching is introduced.
- [ ] Concurrency, duplicate assignments and explicit revocation metadata are handled; all access changes are audited.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

## MGT-F08: Portal application switching and diagnostic orchestration

Estimate: **11 points**, summed from the cards below.

One portal session lists only accessible applications and proxies approved error/log queries through per-app backend credentials. Switch app context without customer-app login or exposing target secrets.

Dependencies: MGT-F04, MGT-F05, MGT-F06, MGT-F07.

### MGT-014 / IBM-0091: Implement application diagnostics client and portal read endpoints

Estimate: **8 points**.

IApplicationDiagnosticsClient HTTP adapter and ManagementDiagnosticsService; GET /applications/{id}/system-errors[/{errorId}] and /system-logs[/{logId}]. Reuse safe target contracts and registered connection resolver.

Touches: IBeam.Management.Services/Diagnostics/ManagementDiagnosticsService.cs, IBeam.Management.Services/Clients/, IBeam.Management.Api/Controllers/.

- [ ] Every portal list/detail request checks current person/app/operation grants before secret lookup or outbound calls.
- [ ] The selected application determines server-side target and credential; browser-provided URLs/keys/organization overrides are ignored or rejected.
- [ ] Target response identity and opaque IDs/cursors remain application-bound; switching apps cannot reuse another app's data or cursor.
- [ ] Target timeouts, rate limits, invalid responses and outages produce safe bounded responses without exposing transport secrets.
- [ ] Service orchestration invokes entity services rather than cross-repository business logic; no customer tables are queried.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

### MGT-015 / IBM-0092: Expose authorized selector contract and verify multi-app session behavior

Estimate: **3 points**.

GET /api/management/applications returns only current authorized entries/capabilities; /me reports management context. Document the future UI dropdown flow; defer portal UI and IBeam.Admin.API changes.

Touches: IBeam.Management.Services/Applications/, IBeam.Management.Api/, IBeam.Tests.Management/, docs/management/.

- [ ] A single named support session can alternate Qurvia/Bindry/MedInsights requests without signing into those customer apps.
- [ ] Selector excludes apps with no active diagnostic permission; revoked access disappears and direct URL reads also fail.
- [ ] Two support organizations and two users with different app assignments cannot see one another's registrations or diagnostics.
- [ ] HTTP integration scenario proves one-app and multi-app grants with errors/logs permissions independently.
- [ ] No portal frontend, host rename or IBeam.Admin.API dependency is required to complete this framework contract.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

## MGT-F09: Management audit and diagnostic data isolation

Estimate: **10 points**, summed from the cards below.

Record support actor, target, operation and result without customer content or secrets. Apply shared safe diagnostic projections and ensure audit durability/retention/access rules are explicit.

Dependencies: MGT-F02, MGT-F03.

### MGT-016 / IBM-0093: Persist management audit and correlation without sensitive snapshots

Estimate: **5 points**.

ManagementAuditEntity/ManagementAudit, repository and sink integration with IServiceOperationExecutor; source operation/context only, no submitted secret or diagnostic payload snapshots.

Touches: IBeam.Management.Services/Audit/, IBeam.Management.Repositories.AzureTable/, IBeam.Management.Api/.

- [ ] Reads, registration, connection changes, invitations and access grants/revocations record actor/organization/app/operation/time/result/correlation as applicable.
- [ ] Target records machine credential ID and correlation; untrusted browser actor headers are never treated as verified human identity.
- [ ] Secrets, hashes, diagnostic payloads, credentials and customer entity snapshots are absent, including denied/failed paths.
- [ ] Support readers cannot modify/delete audit; retention and audit-write failure policy are configurable/documented and tested.
- [ ] Audit configuration avoids recursively logging audit reads/writes into customer diagnostics; target and portal events can be correlated.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

### MGT-017 / IBM-0094: Verify diagnostic source isolation and safe historical-data behavior

Estimate: **5 points**.

Cross-cutting source binding, safe projection policy and negative tests against current SystemErrors/SystemLogs schemas; shared-source migration strategy remains explicit.

Touches: IBeam.Management.Services/Diagnostics/, IBeam.Management.Repositories.AzureTable/Diagnostics/, IBeam.Tests.Management/, docs/management/.

- [ ] Existing errors without tenant identity are treated as app-wide only; per-customer-tenant restricted access is not claimed.
- [ ] Mixed application/environment sources require reliable writer tags and reader predicates; untagged historical records fail closed.
- [ ] All raw messages/exceptions/details and business audit JSON fixtures are tested for data leakage in successful and failed responses.
- [ ] Identifiers/cursors cannot widen source/range/category/deployment filters; diagnostic storage permissions are read-only.
- [ ] Document which historical rows can safely be shown and which require recapture/migration before exposure.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

## MGT-F10: NuGet packaging, integration verification and host handoff

Estimate: **11 points**, summed from the cards below.

Ship consumable framework packages and a reproducible test matrix for separate/shared identity storage and multiple applications. Document the later IBeam.Admin.API instance work without renaming or editing that repository now.

Dependencies: MGT-F01, MGT-F02, MGT-F03, MGT-F04, MGT-F05, MGT-F06, MGT-F07, MGT-F08, MGT-F09.

### MGT-018 / IBM-0095: Verify full API boundary matrix and package consumption

Estimate: **8 points**.

Focused HTTP/service/repository tests, opt-in Azurite diagnostics fixtures and NuGet consumer smoke test across all management packages.

Touches: IBeam.Tests.Management/, IBeam.sln, build/, docs/management/.

- [ ] Automated tests cover anonymous/customer/support-machine substitution, cross-organization/app access, expiry/revocation, permission changes, unsafe payloads and cursor tampering.
- [ ] Provider integration tests use isolated fixtures and do not require production credentials; external prerequisites are explicit.
- [ ] Packages restore/build/pack using repository release conventions and contain intended contracts/README/agent prompts without secrets.
- [ ] A minimal test host consumes generated packages and proves authenticated info, app assignment and safe error/log flow with two isolated targets.
- [ ] Existing Identity, API, logging and service operation tests relevant to changed shared components remain green.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

### MGT-019 / IBM-0096: Document deployment and deferred IBeam.Admin.API instance integration

Estimate: **3 points**.

README/configuration/endpoint examples, table/credential bootstrap and rotation/runbook, application onboarding, troubleshooting and deferred consumer-host handoff.

Touches: docs/management/, IBeam.Management*/README.md.

- [ ] Examples cover separate identity storage and shared storage with distinct tables/credentials, and separate diagnostic-source connection settings.
- [ ] Document package install/registration, trusted auth schemes/audiences, MFA, initial administrator, target credential provisioning and app onboarding.
- [ ] Document safe network exposure, origin allowlists, trusted proxy configuration, secret-store setup, rotation/revocation and audit retention/failure policy.
- [ ] IBeam.Admin.API is explicitly a deferred runnable portal API instance that will reference published NuGet packages; its new name remains undecided.
- [ ] No repository rename, production deployment, NuGet publishing or frontend implementation is performed by this handoff card.

Verification: focused service/HTTP tests; isolated provider fixtures where needed; build/pack affected packages and update registration/endpoint documentation.

## Delivery handoff (2026-10-16)

IBM-0078 through IBM-0096 deliver the original 19-card framework scope (104 points), merged to development card by card. The deployment guide is docs/management/deployment-and-handoff.md and repeatable package-only consumer validation is samples/Management.PackageSmoke. IBeam.Admin.API remains deferred and unchanged.

IBM-0097 adds 3 points to MGT-F05 (11 points across 3 cards), bringing the epic to 20 cards / 107 points. Abram authorized taking over the card and completing the epic; the unavailable external commit was replaced with a fresh implementation on current development, including current portal integration. Independent target settings, safe default output, approved non-Production raw fields and metadata-only audit are documented and tested. All 20 cards are delivered by this handoff; the separate consumer host/UI/rename/deployment remains deferred.
