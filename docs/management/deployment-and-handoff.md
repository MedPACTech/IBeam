# Management API deployment and consumer handoff

IBeam supplies reusable NuGet libraries for a support portal and independently protected diagnostics on each target application. One named support session can manage several registered apps. App assignments grant errors and logs separately; they do not grant direct customer-table access. The separate IBeam.Admin.API repository is the deferred runnable portal API instance that will consume published packages. Its future name is undecided. This delivery does not rename/edit that repository, create a frontend, deploy production resources or manually publish packages. Existing development-package automation follows the normal repository workflow.

## Projects and boundaries

| Package/project | Responsibility |
| --- | --- |
| IBeam.Management | Entities, DTOs and ports; no Identity/ASP.NET/Azure dependencies. |
| IBeam.Management.Services | App registry, verified connections, assignments, selector, portal orchestration, safe readers and isolated management audit. |
| IBeam.Management.Api | Human support /me, app/connection/assignment/selector and portal diagnostic controllers. |
| IBeam.Management.Diagnostics.Api | Target-only machine authentication, info and opt-in errors/logs controllers. |
| IBeam.Management.Repositories.AzureTable | Entity persistence and read-only diagnostic adapters; no service/Identity dependencies. |
| IBeam.Management.Identity.Services / IBeam.Management.Identity.Api | Optional dedicated support Identity realm, MFA, live sessions, invitations and directory. |
| IBeam.Management.Secrets.AzureKeyVault | Optional writable portal secret store using a host-owned SecretClient. |

Use matching published versions from the configured IBeam package feed. Install the portal API and repository packages in the portal host; optionally add Management.Identity.Api and Secrets.AzureKeyVault. Install Diagnostics.Api and repository packages in each target app. Services/contracts arrive transitively. Do not reference framework source projects from the consumer host.

API -> service -> repository remains the flow. ManagementApplicationsController calls IManagementApplicationService; connection controller calls IManagementApplicationConnectionService; assignment controller calls IManagementApplicationAccessService. The optional selector calls IManagementApplicationSelector. ManagementPortalDiagnosticsController calls IManagementDiagnosticsService -> IManagementDiagnosticConnectionResolver -> IApplicationDiagnosticsClient. Application/state/connection ports coordinate entities rather than joining Identity/customer repositories in controllers. Target controllers call ISystemErrorReaderService/ISystemLogReaderService and their bounded metadata readers.

## Identity storage choice

Use a dedicated support host/service collection for IBeam Identity. The provider is not keyed: registering both customer Identity and management Identity in one service collection is rejected. A Qurvia customer and Qurvia support person with the same email remain separate identities and memberships. They do not share OTPs, passwords or sessions. The support organization is not a customer tenant.

Configure IBeam:Management:Identity:OrganizationId with one fixed support-organization GUID. All example values below are nonsecret; inject missing secrets separately. For separate storage:

```json
{
  "IBeam": {
    "Management": {
      "Identity": {
        "OrganizationId": "10000000-0000-0000-0000-000000000001",
        "BootstrapEnabled": false,
        "Storage": { "Mode": "Separate", "TablePrefix": "Management", "ProvisionSchema": false },
        "Jwt": { "Issuer": "IBeam.Management.Identity", "Audience": "ibeam-management", "AccessTokenMinutes": 10, "SessionInactivityMinutes": 30, "SessionAbsoluteLifetimeDays": 1 },
        "Otp": { "CodeLength": 6, "MaxAttempts": 5 },
        "LoginAttempts": { "Enabled": true }
      }
    }
  }
}
```

LoginAttempts configures the existing password-attempt lockout; it must remain enabled. Supply Storage:ConnectionString, Jwt signing settings, Otp:HashSalt and Otp:VerificationTokenSecret through secret-backed configuration. Separate mode requires its own explicit account endpoint when customer configuration is present; it never silently uses customer storage.

For shared storage, set Storage:Mode=Shared and keep a collision-free prefix such as Management. Supply IBeam:Identity:AzureTable:StorageConnectionString and the actual existing customer Identity/access-control table settings for endpoint/name collision checks; a dedicated management Storage:ConnectionString may use restricted credentials to that same account. Prefixes distinguish schema names, not permissions: an account-wide key can still access customer tables. This provider currently uses connection strings; choose a separate account when shared-account credential isolation cannot meet your requirements.

The adapter reuses the Identity schema with distinct physical tables, rather than creating a new SystemUsers/SystemOTP schema. With the default prefix this includes ManagementAspNetUsers, ManagementAspNetIndex, ManagementAspNetRoles, ManagementTenants, ManagementTenantUsers, ManagementUserTenants, ManagementRoles, ManagementOtpChallenges, ManagementAuthSessions, ManagementTenantInvites and the provider's other prefixed tables. Invitation redemption markers live in the prefixed TenantInvites table. Schema provisioning is a trusted deployment operation, not a runtime HTTP action.

## Authentication, MFA and initial administrator

Use AddIBeamManagementIdentityApi(configuration) once in the dedicated portal host, with a host-registered IIdentityCommunicationSender for reliable email MFA/invitations. Do not install generic Identity controllers or call the lower-level management Identity registration again. The named IBeam.Management.Identity bearer scheme validates signature, algorithm, issuer, audience, expiry and current persisted MFA/session/membership/role state on every protected request and refresh. Customer tokens must have a different audience/trust configuration. Required signing/OTP secrets must be dedicated, strong and stored outside source; the runtime validator rejects bootstrap/schema flags, weak OTP settings and excessive session lifetimes.

Trusted one-off setup uses Storage:ProvisionSchema=true with IIdentitySchemaManager.ApplyAsync, then BootstrapEnabled=true with IManagementIdentityBootstrapService.BootstrapAsync. Obtain the initial password through protected operator input. Bootstrap reuses existing state without resetting passwords or resurrecting disabled access. Turn both flags off and drop setup/schema privileges before starting the runtime API. No bootstrap, key-issue, key-rotate or key-revoke HTTP endpoint is installed.

Password login returns an MFA challenge; successful email MFA returns management access/refresh tokens. The default access lifetime is 10 minutes, inactivity 30 minutes, absolute session lifetime one day. Each read checks live app/user assignments independently of token claims. Invite support staff through the optional support administration module; an invitation grants ManagementSupport only and requires a later normal password/MFA login. Administrators may disable other support memberships and revoke sessions; self/admin disable recovery remains trusted tooling.

Bring-your-own-auth is also supported. Instead of Management.Identity.Api, install your validated named human scheme and call AddIBeamManagementApi(configuration, o => o.AuthenticationScheme = "YourSupportScheme"). Supply a trusted IManagementPrincipalProvider and live IManagementSupportUserStateProvider. Issue unambiguous human user GUID, support org GUID and explicit management permissions after real authentication/MFA. Customer roles, an API key in a browser and arbitrary headers are not support authentication. Administrative claims and live-state checks must be maintained by that host; the core packages do not validate an external identity provider for you.

## Portal registrations and stores

After choosing authentication, register:

```csharp
services.AddIBeamManagementConnectionsApi(configuration);
services.AddIBeamManagementApplicationAccessApi();
services.AddIBeamManagementApplicationSelectorApi(configuration); // also installs portal diagnostics
services.AddIBeamManagementAudit(configuration);
// For the optional IBeam Identity adapter only:
services.AddIBeamManagementSupportUsersApi();
```

Use repository extension methods or host-created TableClients for separate ManagementApplications, ManagementApplicationCredentials and ManagementApplicationAccess stores. Register IManagementApplicationCredentialRepository and IManagementApplicationAccessRepository using their Azure Table adapters; AddIBeamManagementAzureTableApplicationStore supplies the app repository. Register AddIBeamManagementAzureTableAuditStore with a separate audit TableClient. Create these tables through deployment tooling. Application table uniqueness uses an atomic app/index transaction; assignment updates and connection activation require returned versions. No registration automatically creates runtime tables.

A writable vault uses AddIBeamManagementKeyVaultSecrets(dedicatedSecretClient). Its namespace is IBeam.Management.Secrets.AzureKeyVault. Configure that client with the intended vault URI, managed identity/secret permissions and safe SDK logging. Portal writes create scoped, versioned secrets; metadata rows hold references only. Never return a stored key/reference in an app DTO. The portal accepts an administrator's key submission only in the write-only connection body over TLS.

For deployment-managed environment secrets, use AddIBeamManagementEnvironmentSecrets with ManagementEnvironmentSecretBinding(orgId, appId, reference, variableName). Bindings are trusted host configuration, not portal input. Then trusted host tooling resolves ManagementApplicationConnectionService and calls ActivateEnvironmentReferenceAsync(appId, expectedVersion). There is no HTTP environment/reference assignment endpoint; PUT connection is rejected in read-only secret mode. In either mode the browser receives app choices and grants, never a key ring. Support users are assigned to apps, not to raw API keys.

Configure IBeam:Management:Connections:ApprovedOrigins with exact root HTTPS origins on port 443, for example https://api.qurvia.app and https://api.bindry.app. Set Connections:TimeoutSeconds (default 10, range 1–30). PortalDiagnostics:ProtectionKey must be a secret 32-byte base64 key, shared among replicas; PortalDiagnostics:TimeoutSeconds defaults to 10, TokenLifetimeMinutes to 15 and MaximumResponseBytes to 262144. Preserve the key across restarts; rotation intentionally expires existing IDs/cursors. Never place secret values in appsettings checked into source.

## Target registrations and independent diagnostics storage

Each app/environment registers its own target deployment identity, credential store and explicit diagnostic source clients:

```csharp
services.AddIBeamManagementAzureTableCredentialStore(targetCredentialTable);
services.AddIBeamManagementDiagnosticsCredentialApi(configuration);
services.AddIBeamManagementAzureTableSystemErrorReader(readOnlyErrorTable);
services.AddIBeamManagementSystemErrorsApi(configuration);
services.AddIBeamManagementAzureTableSystemLogReader(readOnlyLogTable);
services.AddIBeamManagementSystemLogsApi(configuration);
services.AddIBeamManagementAzureTableAuditStore(appendOnlyAuditTable);
services.AddIBeamManagementAudit(configuration);
```

Credential extensions use the repository root namespace; diagnostic repository extensions use IBeam.Management.Repositories.AzureTable.Diagnostics. Machine authentication uses IBeam.Management.Diagnostics.Api.Authentication; error/log API extensions use IBeam.Management.Diagnostics.Api.Controllers. Supplied TableClients are independent of customer/Identity storage registrations and support table-scoped SAS/token credentials.

When configuring storage through options instead of clients, use these exact sections independently:

| Configuration section | Required values |
| --- | --- |
| IBeam:Management:Credentials:Storage | ConnectionString and TableName (default ManagementApiCredentials) |
| IBeam:Management:Diagnostics:Errors:Storage | ConnectionString and full physical TableName |
| IBeam:Management:Diagnostics:Logs:Storage | ConnectionString and full physical TableName |
| IBeam:Management:Diagnostics | ApplicationKey and Environment identifying the target exactly |
| IBeam:Management:Diagnostics:Errors / Logs | SourceMode, explicit matching trust confirmation and an independent 32-byte base64 ProtectionKey |

Choose DedicatedDeployment + DedicatedSourceConfirmed only for verified dedicated histories, or TaggedShared + TrustedTagsConfirmed for reliable writer tags. See [historical/source isolation guidance](diagnostic-source-isolation.md). Diagnostic table names do not inherit the management Identity prefix. Errors are app/environment-wide even when records lack tenant IDs; do not claim customer-tenant filtering. Business audit JSON and tenant logs remain excluded.

Typical secret-backed environment variable names use double underscores: IBeam__Management__Identity__Storage__ConnectionString, IBeam__Management__Identity__Jwt__SigningKey, IBeam__Management__Identity__Otp__HashSalt, IBeam__Management__Identity__Otp__VerificationTokenSecret, IBeam__Management__PortalDiagnostics__ProtectionKey, and independent target credential/error/log storage and protection settings. Load values through deployment secrets; no example credential values are provided here.

## Target credential provisioning and app onboarding

In trusted target tooling, call AddIBeamManagementCredentialProvisioning(configuration), enable IBeam:Management:Credentials:Provisioning:Enabled, supply IManagementCredentialOperatorProvider and the credential write repository. Resolve IManagementCredentialProvisioningService.IssueAsync with a bounded expiry and explicit capabilities: management.diagnostics.info plus independently required management.diagnostics.errors.read and/or management.diagnostics.logs.read. Capture the generated key once into protected secret storage; targets persist its hash. Runtime target registration uses read-only credential validation, not provisioning privileges.

A named portal administrator creates an app/environment, submits that target key through the connection endpoint (or activates an environment reference through trusted tooling), and verifies the returned app/version/capabilities. The info identity must match the registry exactly. Assign named support people to one or many apps with errors/logs permissions. Administrators must also be assigned before reading diagnostics. App selection uses the same support session; no customer-app login is needed.

Rotation on the target invalidates the old hash/version immediately. Update and verify the portal connection using a current app version; plan a coordinated cutover because one credential ID does not offer an overlap window. For overlap, issue a new credential, verify and activate it in the portal, then revoke the old target credential. Failed portal activation keeps its previous pointer; orphaned candidate secrets/rows require explicit operator retention cleanup. Changing an origin detaches its connection. Revoking an app assignment, disabling a support member or disabling an app denies later portal reads immediately; it does not revoke the machine key at the target. Revoke compromised keys through trusted target tooling too.

## Endpoint inventory

| Actor/module | Endpoints |
| --- | --- |
| Support Identity adapter | POST /api/auth/password-login, /2fa/complete-login, /refresh, /logout |
| Human context | GET /api/management/me |
| App selector | GET /api/management/applications (authorized choices when selector installed) |
| App registry admin | GET /api/management/applications/registry; POST /api/management/applications; GET/PATCH /api/management/applications/{applicationId} |
| Verified connection admin | PUT /api/management/applications/{applicationId}/connection; POST .../connection/test |
| Support directory/invitations (opt-in) | GET /api/management/support-users; POST /api/management/support-invitations; POST .../support-invitations/accept; PATCH .../support-users/{userId} with isEnabled=false |
| App assignment admin | GET .../applications/{applicationId}/support-users; PUT/DELETE .../support-users/{userId} (DELETE uses If-Match) |
| Human portal diagnostics | GET .../applications/{applicationId}/system-errors and /system-logs; GET either /{opaqueId} |
| Target machine diagnostics | GET /api/management-diagnostics/info, /system-errors, /system-logs; GET either diagnostic /{opaqueId} |

List diagnostic queries accept fromUtc/toUtc/pageSize/continuationToken; logs add source/level/traceId. Continue while a token exists, including empty pages. Do not copy tokens/IDs between apps, users, operations, changed connections or queries. Portal IDs/cursors have fixed expiry and bind org/user/app/operation/environment/origin/credential/app version. The /registry list remains available for administrators after opting into selector behavior. Without the optional selector, the base /applications GET retains the administrator registry contract. Optional read/support modules remain absent unless registered.

## Network, secrets, audit and operations

Expose the portal on a separate TLS hostname with its own authentication, ingress policy and restricted administrative network as appropriate. Targets accept one dedicated X-IBeam-Management-Key header on diagnostic routes only; customer bearer tokens and human portal tokens cannot substitute. Optional IBeam:Management:Credentials:Network:AllowedNetworks restricts target remote IPs by CIDR; an empty list adds no IP restriction. Treat this as an additional control, alongside credentials and per-operation grants.

If behind a reverse proxy, configure explicit trusted proxy addresses/networks and forwarded header limits, then run that middleware before authentication. Do not trust arbitrary X-Forwarded-For. Configure customer and support CORS origins deliberately. Neither middleware order nor IP allowlists grant a management permission. Keep request bodies, Authorization, API-key headers, OTPs, invitation links, secret-store responses and diagnostic tokens/trace filters out of ingress, APM and custom HTTP logs.

Outbound target calls use exact approved public HTTPS origins, validated public DNS, IP-pinned sockets and normal TLS/SNI. Redirects, proxies, cookies, caller Host headers and automatic trace forwarding are disabled. Private/link-local/loopback destinations are currently rejected; there is no insecure bypass switch. Use a separately reviewed network integration design if private-only targets are required. Ingress restrictions and the outbound approved-origin policy are separate controls.

Configure bounded proxy/server request-line/URL limits large enough for encrypted IDs/cursors (maximum 16384 characters plus route/query overhead), as well as normal body limits. Standard default URI limits may reject long continuations before the framework sees them. Avoid unbounded size increases. Use no-store handling and safe global exception middleware; preserve the framework's expected error filters and caller cancellation. Do not enable detailed provider/SDK error bodies for support responses.

IBeam:Management:Audit:RetentionDays defaults to 90, WriteTimeoutSeconds to 5 and FailOnWriteError to true. Management-only auditing preserves other services' configured audit pipeline. Configure a separate append-only audit table/client and restricted retention operator; table-scoped Add-only credentials or an equivalent broker can enforce append without deletion. ExpiresUtc is retention metadata, not an Azure Table TTL. Deployment-owned cleanup/archive must enforce it. A mutation may have committed before its audit failed; read current state before retrying. Best-effort mode must explicitly set FailOnWriteError=false and monitor management.audit.write_failures. The sink does not write customer diagnostic logs or recurse through services.

Portal-generated X-IBeam-Management-Correlation links target read events to portal events. Targets record authenticated machine credential identity, not a claimed human actor header. Human correlation supplied by a browser is not adopted. No support audit read/mutate/delete endpoint is shipped. Encryption keys, API keys and storage credentials remain independent; rotate each through its owning host/tooling and test denial of the previous value.

## Validation and remaining work

Run the Management test projects with private Azurite endpoints in IBEAM_MANAGEMENT_TEST_STORAGE and IBEAM_MANAGEMENT_TEST_SEPARATE_STORAGE. Never point fixture tests at production. The dedicated Validate Management Boundary workflow provisions local fixtures, rejects skipped tests and runs the management/shared-component matrix plus [generated package validation](../../samples/Management.PackageSmoke/README.md). The package consumer starts a portal and two target TestServers with real isolated Table repositories; it references generated packages only. Its fixture authentication, secret store and in-process transport are not deployment implementations.

All 20 Management API cards, including IBM-0097, are delivered in development. The original framework scope is complete with independent sanitization controls and the current portal integration. See [sanitization settings](diagnostic-sanitization.md) for safe defaults, explicit non-Production raw fields and token invalidation. The IBeam.Admin.API instance and future UI/rename/deployment remain separate deferred work.
