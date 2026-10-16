# IBeam.Management.Api

Reusable portal API library. It does not run a server, own an identity database, or install login endpoints.

The host must first register a dedicated support authentication scheme. It must validate a management-only audience/trust domain and issue management context only for enabled, MFA-completed support members. Ordinary customer tokens must not be accepted by this scheme.

```csharp
// Register "ManagementSupport" with the host's trusted authentication implementation first.
// Configure its management-only token audience/issuer and membership/MFA checks.
builder.Services.AddIBeamManagementApi(builder.Configuration,
    options => options.AuthenticationScheme = "ManagementSupport");

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

Equivalent setting: `IBeam:Management:Api:AuthenticationScheme`. Blank or unregistered schemes fail startup. Registration does not change the host's authentication defaults.

`GET /api/management/me` returns an IBeam success envelope containing `userId` and `managementOrganizationId`. The support scheme must establish exactly one authenticated identity containing:
- `uid`, `sub` or name identifier (aliases must agree).
- `ibeam_management_subject_type=human`.
- `ibeam_management_organization_id=<nonempty GUID>`.
- `ibeam_management_permission=management.identity.read`.

Missing/wrong authentication is 401. Authenticated but invalid management context is 403 with a safe IBeam error envelope. No claims or credential details are included in denial responses.

The base package does not install application registration, app assignments, login/MFA or diagnostic reads automatically. IBeam.Admin.API will later consume published packages as a runnable test instance; that repository is intentionally unchanged.

## Optional application registry

After AddIBeamManagementApi, call AddIBeamManagementApplicationsApi(). Supply IManagementApplicationRepository (for example AddIBeamManagementAzureTableApplicationStore with a dedicated TableClient). Missing dependencies fail startup; registry routes are absent unless explicitly installed.

- GET /api/management/applications?pageSize=50&continuationToken=... (1–100).
- POST /api/management/applications with applicationKey, name, environment, baseUrl.
- GET /api/management/applications/{applicationId}.
- PATCH /api/management/applications/{applicationId} with name, baseUrl, isEnabled, expectedVersion (complete mutable configuration).

Every operation requires a trusted human support identity, management organization GUID and explicit management.applications.admin permission. Organization is resolved from that identity; a body/query organization value cannot change it. Configuration reads are administrator-only; assigned-support app selection is a later module. Registration creates no app access grants or customer memberships.

Keys are lowercase letters/digits/hyphens, maximum 100; environment is up to 100 letters/digits/dot/underscore/hyphen; names up to 200. Key/environment are immutable, unique case-insensitively within the support organization. URL is an HTTPS origin on port 443, without user information, IP literals, path, query or fragment. Registry validation makes no outbound request. Changing an origin detaches its connection. Disabling an app preserves metadata but downstream connections must reject it.

Responses expose a concurrency version; stale PATCH returns 409. Other safe envelopes: 400 invalid input, 403 denied, 404 absent/outside organization, 503 storage unavailable. Responses are no-store; audits contain operation/result/entity identifiers, without request/entity/response snapshots.

## Optional connection endpoints

Call AddIBeamManagementConnectionsApi(configuration) after AddIBeamManagementApi; it also installs the application registry routes. Supply IManagementApplicationCredentialRepository and IManagementSecretStore. Configure IBeam:Management:Connections:ApprovedOrigins as an explicit array of HTTPS origins and TimeoutSeconds (1–30, default 10). Empty/invalid approval configuration or missing dependencies fails startup. No connection routes are present without opting in.

- PUT /api/management/applications/{applicationId}/connection: JSON apiKey and expectedVersion. Requires writable secret storage. The key is accepted once, never returned. The service stores a new secret, reads and validates it, confirms /api/management-diagnostics/info, records verified metadata, then atomically switches the app pointer using its expected version.
- POST /api/management/applications/{applicationId}/connection/test: bodyless; uses only the registered application and its active reference. Client URL, header, host or secret-reference input cannot select a target.

Both require management.applications.admin and the same organization-bound human identity as the registry. Disabled apps are denied. Result: applicationId, version, verifiedUtc and supported capabilities, without key/reference/target body. Invalid destination/key/identity/protocol/capability is 400; denied is 403; missing app is 404; concurrent edits are 409; provider/outage is 503; deadline is 504. Caller cancellation propagates.

Connection controllers disable ASP.NET HTTP logging fields, set no-store and limit request bodies to 16 KiB. Do not install custom middleware or telemetry that captures raw request bodies, secret-store responses or X-IBeam-Management-Key headers. The outbound client has no HTTP-client-factory logging handlers or propagated trace headers. Never submit keys in query strings or URLs.

Changing an app origin clears the active connection. A failed replacement/test preserves the existing pointer; concurrent edits, disablement or rotation cannot be overwritten by pending activation. Old and failed-candidate secrets are retained for separately authorized retention/cleanup tooling. This module does not revoke credentials at the target.

## Optional per-application support assignments

After AddIBeamManagementApi, call AddIBeamManagementApplicationAccessApi(). Register IManagementApplicationAccessRepository and IManagementSupportUserStateProvider; the isolated Identity adapter supplies the latter, or a BYO-auth host implements its live organization/user status port. Missing dependencies fail startup. Routes are absent without opting in.

- GET /api/management/applications/{applicationId}/support-users?pageSize=50&continuationToken=... (1–100).
- PUT /api/management/applications/{applicationId}/support-users/{userId}: permissions array and optional expectedVersion. Omit expectedVersion only for a new assignment; updates/regrants require the returned version.
- DELETE /api/management/applications/{applicationId}/support-users/{userId}: If-Match header containing the returned assignment version. Revocation retains metadata and denies future reads.

Only management.diagnostics.errors.read and management.diagnostics.logs.read are accepted, independently; empty, duplicate, unknown and wildcard grants are rejected. A person may have zero, one or many app assignments. Both app and recipient must currently belong to the authenticated management organization and be enabled. No customer membership or target credentials are changed.

Administration requires management.application-access.admin plus a live management administrator state. Granting/removing error access also requires management.application-access.delegate.errors; log access requires management.application-access.delegate.logs. A caller must hold the ceiling for both existing and proposed permissions. Reader tokens cannot grant themselves access, and customer Owner/Admin roles imply none of these permissions. The optional Identity adapter rebuilds these explicit permissions from the live ManagementAdministrator catalog role.

IManagementApplicationAccessService.RequireAsync(appId, permission) is the service boundary for later portal readers: it rechecks organization, current human user, app and persisted assignment on every call, without caching. Even administrators require an assignment to read diagnostics. Revocation, user/organization disablement and app disablement therefore take effect without token refresh. This feature installs assignment administration; portal switching/proxy read routes remain IBM-0091/0092.

Responses are no-store and use the existing safe IBeam envelopes: 400 invalid request, 403 denied scope/state/permission, 404 missing assignment on revoke, 409 duplicate/stale update, 503 provider outage. Operation audit records and assignment lifecycle metadata omit secrets and diagnostic payloads; configure a durable audit sink for persistent operation history.
