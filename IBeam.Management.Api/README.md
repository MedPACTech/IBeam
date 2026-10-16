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
