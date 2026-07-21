# IBeam.Identity.Api

Reusable identity API module for OTP, password, OAuth, token, and session endpoints.

## Narrative Introduction

This package is for API hosts that want to expose identity endpoints quickly with sensible defaults. It composes identity services, Azure-backed repository providers, communications providers, and JWT authentication wiring into a single startup flow while still allowing host-level overrides.

## Features and Components

- DI entry points:
  - `AddIBeamIdentityApi(IConfiguration)`
  - `AddIBeamIdentityApiControllers()`
- pre-wired dependencies:
  - identity services and auth flow services
  - Azure Table identity repository provider
  - Azure Communications email and SMS providers
  - JWT authentication and authorization configuration
- controller endpoints in `AuthController` covering OTP/password/OAuth/token/session flows
  - `RolesController` for tenant role CRUD + user role grant/revoke
  - `TenantInvitesController` for tenant invitation create/list/get/resend/revoke/preview/accept
  - `PermissionMappingsController` for permission catalog + tenant permission->role mappings
  - `AccessControlController` for access catalog, subject grants, access checks, and current-user access context
  - role authorization attributes:
    - `[AllowRoles("owner","admin")]` (role-name claims)
    - `[AllowRoleIds("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9")]` (role-id claims)

## Dependencies

- Internal packages:
  - `IBeam.Communications`
  - `IBeam.Communications.Email.AzureCommunications`
  - `IBeam.Communications.Sms.AzureCommunications`
  - `IBeam.Identity.Repositories.AzureTable`
  - `IBeam.Identity.Services`
- External packages:
  - `Microsoft.AspNetCore.Authentication.JwtBearer`
  - `System.IdentityModel.Tokens.Jwt`
  - `Microsoft.AspNetCore.App` framework reference

## Quick Start

```csharp
builder.Services.AddIBeamIdentityApi(builder.Configuration);
builder.Services.AddIBeamAccessControl(options =>
{
    options.Modules.AddRange(HubbslyModules.All);
    options.ResourceCatalogProviders.Add<HubbslyAccessCatalogProvider>();
});
builder.Services.AddIBeamIdentityApiControllers();
```

## Tenant Provisioning Configuration

`AddIBeamIdentityApi(builder.Configuration)` binds the same tenant provisioning options used by the core identity services.

Workspace-per-user behavior is the default:

```json
{
  "IBeam": {
    "Identity": {
      "TenantProvisioning": {
        "Mode": "AutoCreateTenantForNewUser"
      }
    }
  }
}
```

For a single-tenant API deployment, configure a default tenant explicitly:

```json
{
  "IBeam": {
    "Identity": {
      "TenantProvisioning": {
        "Mode": "UseDefaultTenant",
        "DefaultTenantId": "225925cc-995e-4584-a63b-4f2cb4f38f6f",
        "AutoLinkUserToDefaultTenant": true,
        "AutoLinkRoleNames": [ "Member" ]
      }
    }
  }
}
```

For strict membership-only auth:

```json
{
  "IBeam": {
    "Identity": {
      "TenantProvisioning": {
        "Mode": "RequireExistingTenant",
        "DefaultTenantId": "225925cc-995e-4584-a63b-4f2cb4f38f6f"
      }
    }
  }
}
```

In `RequireExistingTenant`, OTP/password/OAuth endpoints fail clearly when a user is not linked to the requested/configured tenant. They do not create a tenant from the auth flow.

## Authentication Patterns

The API exposes multiple sign-in styles against the same underlying user:

- SMS OTP: `POST /api/auth/startotp`, `POST /api/auth/completeotp`
- Email OTP: `POST /api/auth/startotp`, `POST /api/auth/completeotp`
- Email/password registration: `POST /api/auth/start-email-password-registration`, `POST /api/auth/complete-email-password-registration`
- Email/password login: `POST /api/auth/password-login`
- Link email/password to current user: `POST /api/auth/email-password/link/start`, `POST /api/auth/email-password/link/complete`
- Link phone to current user: `POST /api/auth/phone/link/start`, `POST /api/auth/phone/link/complete`
- 2FA setup/login: `POST /api/auth/2fa/setup/start`, `POST /api/auth/2fa/setup/complete`, `POST /api/auth/2fa/complete-login`
- OAuth: `GET /api/auth/oauth/start`, `POST /api/auth/oauth/complete`

This allows a product to start users with the lowest-friction credential and add stronger or alternate credentials later. For example, a user can sign up with SMS OTP, then add email/password from an authenticated session. Future logins by SMS OTP, email OTP, or email/password resolve to the same `UserId`.

## Request Examples

### SMS OTP

```http
POST /api/auth/startotp
Content-Type: application/json

{ "destination": "16145551212" }
```

```http
POST /api/auth/completeotp
Content-Type: application/json

{
  "challengeId": "8e2c6d3b-4fa1-4c5f-b2df-4a2790f5fbef",
  "destination": "16145551212",
  "code": "123456",
  "displayName": "Adam"
}
```

### Add email/password to the current SMS user

```http
POST /api/auth/email-password/link/start
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "email": "adam@test.com",
  "resetUrlBase": "https://app.example.com/finish-email-link"
}
```

```http
POST /api/auth/email-password/link/complete
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "email": "adam@test.com",
  "challengeId": "f15c846f-88dc-40fb-91e5-62a6f9f47a46",
  "verificationToken": "token-from-email",
  "newPassword": "new secure password"
}
```

### Add SMS to the current email user

```http
POST /api/auth/phone/link/start
Authorization: Bearer {accessToken}
Content-Type: application/json

{ "phoneNumber": "16145551212" }
```

```http
POST /api/auth/phone/link/complete
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "phoneNumber": "16145551212",
  "challengeId": "58ed50e1-a932-4bb5-a37c-2378a514eb79",
  "code": "123456"
}
```

## Role Management Endpoints

- `GET /api/tenants/{tenantId}/roles`
- `POST /api/tenants/{tenantId}/roles`
- `PUT /api/tenants/{tenantId}/roles/{roleId}`
- `DELETE /api/tenants/{tenantId}/roles/{roleId}`
- `POST /api/tenants/{tenantId}/roles/grant`
- `POST /api/tenants/{tenantId}/roles/revoke`
- `GET /api/tenants/{tenantId}/users/{userId}/roles`

Role management endpoints require an authenticated tenant token for the route tenant and either a configured tenant management role or a configured role-management permission claim.

## Configurable Management Authorization

The Identity API uses `IBeam:Identity:AccessControl` for built-in admin endpoint access. Defaults preserve the small-application path:

- owner roles: `Owner`
- admin roles: `Administrator`, `Admin`
- auth-attempt support roles: `PlatformAdmin`, `platform-admin`, `Support`

Developers can replace or extend these defaults in configuration or code. The API checks `tid`, `tenant_id`, or the Microsoft tenant id claim against the route tenant, then accepts configured role names from `role`, `roles`, or `ClaimTypes.Role` claims. It also accepts configured permission names from `permission`, `permissions`, `scope`, or `scp` claims. The built-in permission defaults include broad `*.manage` names and concrete `IBeamOperation` names such as `identity.tenantinvites.create` and `identity.apicredentials.rotate`.

```json
{
  "IBeam": {
    "Identity": {
      "AccessControl": {
        "OwnerRoleNames": [ "Owner" ],
        "AdminRoleNames": [ "Administrator", "Admin", "RegionalAdmin" ],
        "TenantManagementPermissionNames": [ "identity.tenants.manage" ],
        "TenantUserManagementPermissionNames": [ "identity.tenantusers.manage", "identity.tenantinvites.manage" ],
        "TenantRoleManagementPermissionNames": [ "identity.tenantroles.manage" ],
        "TenantAccessControlManagementPermissionNames": [ "identity.accesscontrol.manage" ],
        "ApiCredentialManagementPermissionNames": [ "identity.apicredentials.manage" ],
        "AuthAttemptManagementRoleNames": [ "PlatformAdmin", "Support" ],
        "AuthAttemptManagementPermissionNames": [ "identity:auth-attempts:unlock" ]
      }
    }
  }
}
```

Code configuration works the same way:

```csharp
builder.Services.AddIBeamAccessControl(options =>
{
    options.AdminRoleNames.Clear();
    options.AdminRoleNames.Add("RegionalAdmin");
    options.AdminRoleNames.Add("ClinicAdministrator");

    options.TenantUserManagementPermissionNames.Clear();
    options.TenantUserManagementPermissionNames.Add("identity.users.invite");
});
```

API credential management endpoints remain human-only: API credential principals are rejected even when they carry matching roles or permissions.

## Tenant Invitation Endpoints

Tenant invite endpoints let owners/admins onboard users by email or SMS while preserving tenant context. The recipient may be an existing global IBeam user or a brand-new user; acceptance links the resolved identity user to the invited tenant.

Admin endpoints require an authenticated tenant token for the route tenant and either a configured tenant management role or a configured tenant-user/invite management permission claim.

```http
POST /api/tenants/{tenantId}/invites
GET  /api/tenants/{tenantId}/invites
GET  /api/tenants/{tenantId}/invites/{inviteId}
POST /api/tenants/{tenantId}/invites/{inviteId}/resend
POST /api/tenants/{tenantId}/invites/{inviteId}/revoke
```

Anonymous recipient endpoints:

```http
GET  /api/invites/{tokenOrCode}/preview
POST /api/invites/accept
```

Create an email invite:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/invites
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "destinationType": "email",
  "email": "ada@example.com",
  "displayName": "Ada Lovelace",
  "firstName": "Ada",
  "lastName": "Lovelace",
  "roleNames": ["Member"],
  "roleIds": [],
  "setAsDefaultTenant": true,
  "redirectUrl": "https://app.example.com/invites/accept",
  "metadata": {
    "source": "admin-users"
  },
  "accessGrants": [
    {
      "resourceType": "module",
      "resourceId": "work",
      "accessLevel": "view",
      "expirationUtc": null,
      "metadata": {
        "reason": "initial invite access"
      }
    }
  ]
}
```

Create an SMS invite:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/invites
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "destinationType": "sms",
  "phoneNumber": "+16145551212",
  "displayName": "Care Coordinator",
  "roleNames": ["Member"],
  "setAsDefaultTenant": true
}
```

Preview an invite before authentication:

```http
GET /api/invites/{inviteToken}/preview
```

Accept with an existing authenticated session:

```http
POST /api/invites/accept
Authorization: Bearer {recipientAccessToken}
Content-Type: application/json

{
  "inviteToken": "{inviteToken}",
  "mode": "existing-session",
  "setAsDefaultTenant": true
}
```

Accept with OTP verification:

```http
POST /api/invites/accept
Content-Type: application/json

{
  "inviteToken": "{inviteToken}",
  "mode": "otp",
  "challengeId": "58ed50e1-a932-4bb5-a37c-2378a514eb79",
  "code": "123456",
  "displayName": "Ada Lovelace"
}
```

Acceptance validates the invite token, verifies the invited destination, resolves or creates the identity user, links the user to the invite tenant, applies role ids/names, ensures the host-owned user extension row, applies optional access grants when access-control services are registered, marks the invite redeemed, and returns a tenant-scoped auth result.

Host applications should override `ITenantInviteUrlBuilder` and `ITenantInviteMessageFactory` when they need branded templates, product-specific URLs, or richer template models. IBeam does not own custom invite screens, billing/license policy, or app-specific profile fields.

## API Credential Role Catalog

API credential role names are machine/agent scopes assigned directly to API credentials. They are
separate from tenant user membership roles.

- `GET /api/api-credentials/role-catalog`

The catalog endpoint returns structured entries with name, display name, description, category,
and built-in/pattern/assignable flags. Built-ins include `API`, `tool:mcp`, `api-scope:*`,
`api-scope:work`, `api-scope:contacts`, `api-scope:money`, and `agent:*`.

Configured host entries can be added under `IBeam:Identity:ApiCredentials:RoleCatalog`.

## First-Class API Credential Access

API credentials are machine principals. IBeam keeps the existing role-string behavior for compatibility, but also normalizes credential roles into structured API scopes, tool scopes, agent bindings, resource constraints, claims, and evaluated access context.

Legacy role strings remain valid:

```text
API
api-scope:work
api-scope:money
api-agent:codex
tool:mcp
product:hubbsly
project:*
```

IBeam normalizes them as:

- `api-scope:work` -> `apiScopes: ["work"]`
- `tool:mcp` -> `tools: ["mcp"]`
- `api-agent:codex` or `agent:codex` -> `allowedAgentKeys: ["codex"]`
- `product:hubbsly` -> resource access entry for product `hubbsly`
- `project:*` -> wildcard project access entry

### API Credential Scope Catalog

Register API modules and tools in code:

```csharp
builder.Services.AddIBeamApiCredentials(options =>
{
    options.KeyPrefix = "hbk";

    options.Scopes.AddModule("work", "Work API", "Allows access to Work API features.");
    options.Scopes.AddModule("planning", "Planning API", "Allows access to Planning API features.");
    options.Scopes.AddModule("products", "Products API", "Allows access to Products and Projects API features.");
    options.Scopes.AddModule("ops", "Operations API", "Allows access to Operations API features.");
    options.Scopes.AddModule("content", "Content API", "Allows access to Content API features.");
    options.Scopes.AddModule("money", "Money API", "Allows access to Money API features.");
    options.Scopes.AddModule("contacts", "Contacts API", "Allows access to Contacts API features.");

    options.Scopes.AddTool("mcp", "MCP Tools", "Allows access to the MCP tools endpoint.");
});
```

Fetch the assignable catalog:

```http
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/api-credentials/scope-catalog
Authorization: Bearer {ownerOrAdminTenantToken}
```

Example response item:

```json
{
  "key": "work",
  "displayName": "Work API",
  "description": "Allows access to Work API features.",
  "category": "module",
  "isAssignable": true,
  "isWildcardCapable": true,
  "requiredParentScope": null,
  "moduleKey": "work",
  "resourceType": null
}
```

### Tenant-Scoped Credential Management

The existing `/api/api-credentials` routes still work for the current tenant in the caller's token. New tenant-scoped routes are also available:

```http
GET    /api/tenants/{tenantId}/api-credentials
GET    /api/tenants/{tenantId}/api-credentials/{credentialId}
POST   /api/tenants/{tenantId}/api-credentials
PUT    /api/tenants/{tenantId}/api-credentials/{credentialId}
DELETE /api/tenants/{tenantId}/api-credentials/{credentialId}
```

Create a credential with structured agent metadata and backward-compatible roles:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/api-credentials
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "displayName": "Codex Work Agent",
  "description": "Automation credential for Codex work-board tasks.",
  "agentKey": "codex",
  "agentDisplayName": "Codex Agent",
  "allowedAgentKeys": ["codex"],
  "roleNames": ["API", "api-scope:work", "tool:mcp", "api-agent:codex"],
  "roleIds": [],
  "expiresUtc": "2026-09-20T00:00:00Z"
}
```

The raw API key is returned only once:

```json
{
  "credential": {
    "id": "1dff7a6a-545a-4790-aec1-37f5c5182fb1",
    "tenantId": "225925cc-995e-4584-a63b-4f2cb4f38f6f",
    "displayName": "Codex Work Agent",
    "agentKey": "codex",
    "roleNames": ["API", "api-agent:codex", "api-scope:work", "tool:mcp"],
    "isActive": true
  },
  "apiKey": "hbk_xxxxxxxxxxxxxxxxxxxxx"
}
```

Rotate, revoke, and reactivate:

```http
POST /api/tenants/{tenantId}/api-credentials/{credentialId}/rotate
POST /api/tenants/{tenantId}/api-credentials/{credentialId}/revoke
POST /api/tenants/{tenantId}/api-credentials/{credentialId}/activate
```

Rotation returns a new raw secret once and clears revocation metadata. Revocation disables authentication until rotation or activation.

### Credential Access

Credential access can be managed separately from display metadata:

```http
GET /api/tenants/{tenantId}/api-credentials/{credentialId}/access
PUT /api/tenants/{tenantId}/api-credentials/{credentialId}/access
```

Example update:

```http
PUT /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/api-credentials/1dff7a6a-545a-4790-aec1-37f5c5182fb1/access
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "roleNames": ["API"],
  "roleIds": [],
  "apiScopes": ["work", "products"],
  "toolScopes": ["mcp"],
  "allowedAgentKeys": ["codex"]
}
```

This is stored in backward-compatible role names such as `api-scope:work`, `tool:mcp`, and `api-agent:codex`, while the access endpoint returns a structured context.

API credential access checks can include module/API scope, permission, resource, and requested agent requirements in one request:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/check
Authorization: Bearer {tenantScopedAccessToken}
Content-Type: application/json

{
  "subjectType": "api-credential",
  "subjectId": "1dff7a6a-545a-4790-aec1-37f5c5182fb1",
  "module": "work",
  "permission": "work.update",
  "resourceType": "project",
  "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
  "accessLevel": "edit",
  "agentKey": "codex"
}
```

The check requires all supplied dimensions to pass: requested agent, module/API scope, permission, and resource access.

### API Credential Resource Grants

API credentials use the same generic access grant system as users:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/grants
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "subjectType": "api-credential",
  "subjectId": "1dff7a6a-545a-4790-aec1-37f5c5182fb1",
  "resourceType": "project",
  "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
  "accessLevel": "edit"
}
```

Module/API scopes determine which module APIs can be called. Resource grants narrow which products, projects, or records the credential may touch. Host domain rules should still enforce relationships such as product-to-project containment.

### Evaluated Credential Context

For the current API credential:

```http
GET /api/api-credentials/me/access
X-API-Key: {rawApiKey}
X-Agent-Key: codex
```

The generalized endpoint also returns credential access context when the caller is authenticated by API key:

```http
GET /api/access/me
X-API-Key: {rawApiKey}
X-Agent-Key: codex
```

Example response:

```json
{
  "principalType": "api-credential",
  "tenantId": "225925cc-995e-4584-a63b-4f2cb4f38f6f",
  "credentialId": "1dff7a6a-545a-4790-aec1-37f5c5182fb1",
  "credentialName": "Codex Work Agent",
  "agentKey": "codex",
  "agentDisplayName": "Codex Agent",
  "isActive": true,
  "roles": ["API", "api-scope:work", "tool:mcp", "api-agent:codex"],
  "roleIds": [],
  "permissions": ["work.read", "work.update"],
  "apiScopes": ["work"],
  "tools": ["mcp"],
  "allowedAgentKeys": ["codex"],
  "resources": {
    "project": [
      {
        "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
        "slug": null,
        "accessLevel": "edit",
        "source": "grant"
      }
    ]
  },
  "capabilities": {
    "canUseMcp": true,
    "canAccessWorkApi": true,
    "canActAsRequestedAgent": true
  }
}
```

### Agent Selectors

IBeam understands these headers when validating a requested agent:

```http
X-Agent-Key: codex
X-Api-Agent: codex
X-Api-Agent-Key: codex
```

It also accepts claim selectors:

```text
agent_key
api_agent_key
apiAgentKey
apiAgentId
```

### Normalized API Credential Claims

API key authentication emits compatibility role claims and structured claims:

```text
tid = {tenantId}
tenant_id = {tenantId}
sub = {credentialId}
uid = {credentialId}
api_subject_type = credential
principal_type = api-credential
api_credential_id = {credentialId}
api_credential_name = {displayName}
agent_key = {agentKey}
api_agent_key = {agentKey}
role = API
role = api-scope:work
role = tool:mcp
scope = work
tool = mcp
allowed_agent_key = codex
```

### API Credential Authorization Policies

Dynamic policy names are available for API credential access:

```csharp
[Authorize(Policy = "RequireApiScope:work")]
[Authorize(Policy = "RequireTool:mcp")]
[Authorize(Policy = "RequireAgent:codex")]
```

Imperative checks are available through `IApiCredentialAccessService`:

```csharp
public sealed class WorkMcpService
{
    private readonly IApiCredentialAccessService _access;

    public WorkMcpService(IApiCredentialAccessService access)
    {
        _access = access;
    }

    public async Task InvokeAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        await _access.RequireToolAccessAsync(principal, "mcp", ct);
        await _access.RequireApiScopeAsync(principal, "work", ct);
        await _access.RequireAgentAccessAsync(principal, "codex", ct);
    }
}
```

## Permission Management Endpoints

- `GET /api/tenants/{tenantId}/permissions/catalog`
- `GET /api/tenants/{tenantId}/permissions/mappings`
- `PUT /api/tenants/{tenantId}/permissions/mappings/by-name`
- `PUT /api/tenants/{tenantId}/permissions/mappings/by-id`
- `DELETE /api/tenants/{tenantId}/permissions/mappings/by-name?permissionName=...`
- `DELETE /api/tenants/{tenantId}/permissions/mappings/by-id/{permissionId}`

Permission mutation behavior is controlled by `IBeam:Identity:RoleManagement`:
- `PermissionMode`: `HardCoded`, `Repository`, `Configuration`, `Hybrid`
- `AllowTenantPermissionMapMutation`
- `AllowTenantRoleCreation`
- `AllowTenantRoleMutation`

## Unified Access Control

IBeam access control lets a host application use IBeam as the source of truth for tenant roles, role permissions, user or credential grants, module access, resource access, and the evaluated current-user access context.

The split of responsibility is:

- IBeam owns tenant users, tenant roles, role assignments, permission mappings, access grants, access evaluation, and optional HTTP administration endpoints.
- The host app owns app-specific module keys, permission names, resource types, dynamic resource catalogs, and domain service enforcement.

This supports both role-heavy applications and hybrid applications:

- Role-heavy: roles such as `Work Viewer`, `Product Manager`, and `Billing Manager` imply modules and permissions.
- Hybrid: broad roles such as `Owner`, `Admin`, and `Application` are combined with explicit grants like `module:work:view`, `product:{id}:view`, and `project:{id}:edit`.

### Access-Control Startup

Register the normal Identity API, then register access-control modules and dynamic catalog providers. `AddIBeamIdentityApi(...)` already registers Identity services and the Azure Table provider. `AddIBeamAccessControl(...)` layers host-specific access declarations on top.

```csharp
using IBeam.Identity.Api;
using IBeam.Identity.Api.DependencyInjection;
using IBeam.Identity.Models;
using IBeam.Identity.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIBeamIdentityApi(builder.Configuration);

builder.Services.AddIBeamAccessControl(options =>
{
    options.OwnerRoleNames.Clear();
    options.OwnerRoleNames.Add("Owner");

    options.AdminRoleNames.Clear();
    options.AdminRoleNames.Add("Administrator");
    options.AdminRoleNames.Add("Admin");

    options.ApplicationRoleNames.Clear();
    options.ApplicationRoleNames.Add("Application");

    options.AccessLevels.Clear();
    options.AccessLevels.Add(new AccessLevelDefinition("view", 0, "View"));
    options.AccessLevels.Add(new AccessLevelDefinition("edit", 10, "Edit"));
    options.AccessLevels.Add(new AccessLevelDefinition("manage", 20, "Manage"));

    options.Modules.AddRange(HubbslyModules.All);
    options.ResourceCatalogProviders.Add<HubbslyAccessCatalogProvider>();
});

builder.Services.AddIBeamIdentityPermissionCatalog(catalog =>
{
    catalog.AddPermission(
        "users.view",
        resource: "Users",
        description: "View tenant users.",
        label: "View users",
        category: "Administration");

    catalog.AddPermission(
        "users.manage",
        resource: "Users",
        description: "Invite, disable, and manage tenant users.",
        label: "Manage users",
        category: "Administration");

    catalog.AddPermission(
        "work.view",
        resource: "Work",
        description: "Open the work board.",
        label: "View work",
        category: "Work",
        moduleKey: "work",
        accessLevel: "view");

    catalog.AddPermission(
        "products.edit",
        resource: "Products",
        description: "Edit product records.",
        label: "Edit products",
        category: "Products",
        moduleKey: "products",
        accessLevel: "edit");
});

builder.Services.AddIBeamIdentityPermissionMappings(mappings =>
{
    mappings.AllowRolesForPermission("users.view", "Owner", "Administrator", "Admin");
    mappings.AllowRolesForPermission("users.manage", "Owner", "Administrator", "Admin");
    mappings.AllowRolesForPermission("work.view", "Owner", "Administrator", "Work Viewer");
    mappings.AllowRolesForPermission("products.edit", "Owner", "Administrator", "Product Manager");
});

builder.Services.AddIBeamIdentityApiControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapIBeamAccessControlApi();

app.Run();
```

### Static Module Declarations

Modules are top-level app surfaces, usually navigation entries or major product areas. Modules can be granted directly, implied by role names, implied by role IDs, or implied by permissions.

```csharp
using IBeam.Identity.Models;

public static class HubbslyModules
{
    public static readonly IReadOnlyList<AccessModuleDefinition> All =
    [
        new(
            Key: "work",
            Label: "Work",
            Description: "Work board access.",
            SupportedAccessLevels: ["view", "edit", "manage"],
            ImpliedByRoleNames: ["Owner", "Administrator", "Work Viewer"],
            ImpliedByPermissionNames: ["work.view"]),

        new(
            Key: "products",
            Label: "Products",
            Description: "Product catalog access.",
            SupportedAccessLevels: ["view", "edit", "manage"],
            ImpliedByRoleNames: ["Owner", "Administrator", "Product Manager"],
            ImpliedByPermissionNames: ["products.view", "products.edit"]),

        new(
            Key: "money",
            Label: "Money",
            Description: "Billing and payments access.",
            SupportedAccessLevels: ["view", "manage"],
            ImpliedByRoleNames: ["Owner", "Administrator", "Billing Manager"],
            ImpliedByPermissionNames: ["billing.manage"])
    ];
}
```

### Layered Access Catalog

Definitions/options are layered, assignments are persisted, and evaluation is unified.

The effective catalog endpoint is what a frontend should use to decide which fine-grained access items can be assigned:

```http
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog
Authorization: Bearer {ownerOrAdminTenantToken}
```

Optional filters:

```http
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog?subjectType=user
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog?subjectType=api-credential
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog?category=resource
```

Catalog items include their source, assignability, mutability, enabled state, subject type, and optional resource metadata. Duplicate keys are resolved in this precedence order:

```text
tenantOverride
tenantDb
hostProvider
hostConfig
ibeamDefault
```

Tenant roles are intentionally not part of the access-catalog contract. Use `GET /api/tenants/{tenantId}/roles` for the canonical tenant role list, role ids, and role descriptions. Access catalog remains focused on fine-grained permissions, operations, modules, API scopes, tools, agents, resources, and access levels.

Example response:

```json
{
  "permissions": [
    {
      "key": "work.view",
      "label": "View work",
      "description": "Open the work board.",
      "category": "permission",
      "source": "hostConfig",
      "isAssignable": true,
      "isMutable": false,
      "isEnabled": true,
      "subjectTypes": ["user"],
      "supportedAccessLevels": ["view"]
    }
  ],
  "modules": [
    {
      "key": "work",
      "label": "Work",
      "description": "Work board access.",
      "category": "module",
      "source": "hostConfig",
      "isAssignable": true,
      "isMutable": false,
      "isEnabled": true,
      "subjectTypes": ["user", "api-credential"],
      "resourceType": "module",
      "resourceId": "work",
      "supportedAccessLevels": ["view", "edit", "manage"]
    }
  ],
  "apiScopes": [
    {
      "key": "work",
      "label": "Work API",
      "description": "Allows access to Work API features.",
      "category": "apiScope",
      "source": "ibeamDefault",
      "isAssignable": true,
      "isMutable": false,
      "isEnabled": true,
      "subjectTypes": ["api-credential"],
      "resourceId": "work"
    }
  ],
  "tools": [
    {
      "key": "mcp",
      "label": "MCP Tools",
      "category": "tool",
      "source": "ibeamDefault",
      "isAssignable": true,
      "isMutable": false,
      "isEnabled": true,
      "subjectTypes": ["api-credential"]
    }
  ],
  "agents": [],
  "resources": [
    {
      "key": "project:24e4785d-d558-4511-a879-b70d5c88cd51",
      "label": "Platform",
      "category": "resource",
      "source": "hostProvider",
      "isAssignable": true,
      "isMutable": false,
      "isEnabled": true,
      "subjectTypes": ["user", "api-credential"],
      "resourceType": "project",
      "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
      "parentResourceType": "product",
      "parentResourceId": "hubbsly",
      "supportedAccessLevels": ["view", "edit", "manage"]
    }
  ],
  "accessLevels": [
    {
      "key": "view",
      "label": "View",
      "category": "accessLevel",
      "source": "ibeamDefault",
      "isAssignable": true,
      "isMutable": false,
      "isEnabled": true,
      "rank": 0
    }
  ],
  "resourceTypes": ["project"]
}
```

Tenant-specific catalog additions or allowed overrides are managed with:

```http
GET    /api/tenants/{tenantId}/access-catalog/overrides
POST   /api/tenants/{tenantId}/access-catalog/overrides
PUT    /api/tenants/{tenantId}/access-catalog/overrides/{catalogItemId}
DELETE /api/tenants/{tenantId}/access-catalog/overrides/{catalogItemId}
```

Example tenant DB addition:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog/overrides
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "key": "agent:atlas",
  "label": "Atlas Agent",
  "description": "Tenant-approved automation agent.",
  "category": "agent",
  "isAssignable": true,
  "isMutable": true,
  "isEnabled": true,
  "subjectTypes": ["api-credential"]
}
```

IBeam rejects attempts to mutate a matching non-mutable system or host catalog item. New tenant additions are stored in the configured catalog override store; the Azure Table provider persists them in `AccessCatalogOverrides`.

### Dynamic Resource Catalog Provider

Use `IIBeamAccessCatalogProvider` when access can be granted to tenant-specific resources such as products, projects, work boards, contacts, records, or facilities. IBeam does not need to know the app's domain schema; the host projects assignable resources into IBeam's generic catalog shape.

```csharp
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

public sealed class HubbslyAccessCatalogProvider : IIBeamAccessCatalogProvider
{
    private readonly IProductRepository _products;
    private readonly IProjectRepository _projects;

    public HubbslyAccessCatalogProvider(
        IProductRepository products,
        IProjectRepository projects)
    {
        _products = products;
        _projects = projects;
    }

    public async Task<IReadOnlyList<AccessCatalogResource>> GetResourcesAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var resources = new List<AccessCatalogResource>();

        var products = await _products.ListForTenantAsync(tenantId, ct);
        resources.AddRange(products.Select(product => new AccessCatalogResource(
            ResourceType: "product",
            ResourceId: product.ProductId.ToString("D"),
            Label: product.Name,
            Description: product.Description,
            SupportedAccessLevels: ["view", "edit", "manage"])));

        var projects = await _projects.ListForTenantAsync(tenantId, ct);
        resources.AddRange(projects.Select(project => new AccessCatalogResource(
            ResourceType: "project",
            ResourceId: project.ProjectId.ToString("D"),
            Label: project.Name,
            Description: project.Status,
            SupportedAccessLevels: ["view", "edit", "manage"],
            ParentResourceType: "product",
            ParentResourceId: project.ProductId.ToString("D"))));

        return resources;
    }
}
```

For catalog entries that are not resources, use `IIBeamAccessCatalogItemProvider`:

```csharp
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

public sealed class HubbslyAgentCatalogProvider : IIBeamAccessCatalogItemProvider
{
    public Task<IReadOnlyList<AccessCatalogItem>> GetCatalogItemsAsync(
        Guid tenantId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AccessCatalogItem>>(
        [
            new(
                Key: "codex",
                Label: "Codex",
                Description: "Coding and repository automation agent.",
                Category: AccessCatalogCategories.Agent,
                Source: AccessCatalogSources.HostProvider,
                IsAssignable: true,
                IsMutable: false,
                IsEnabled: true,
                SubjectTypes: [AccessSubjectTypes.ApiCredential])
        ]);
}
```

Resource grants do not imply module or API access. A user or API credential with `project:24e4785d...:edit` still needs the relevant module grant, permission, or API scope such as `module:work` or `api-scope:work`.

### Permission Catalog Examples

The permission catalog endpoint returns permissions discovered from attributes, configured catalog entries, and permission mappings. Configured entries can include labels, descriptions, categories, assignability, and optional module or resource associations.

```http
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/permissions/catalog
Authorization: Bearer {ownerOrAdminTenantToken}
```

Example response item:

```json
{
  "permissionName": "products.edit",
  "permissionId": null,
  "source": "configuration:catalog",
  "resource": "Products",
  "description": "Edit product records.",
  "label": "Edit products",
  "category": "Products",
  "isAssignable": true,
  "moduleKey": "products",
  "resourceType": null,
  "resourceId": null,
  "accessLevel": "edit"
}
```

### Operation Permissions

Operation permissions protect business actions. They complement module and resource access:

```text
Module access: can enter this area?
Resource access: can access this specific thing?
Operation permission: can perform this action?
```

For service code, the clearest enforcement model is still explicit:

```csharp
public sealed class ProjectService
{
    private readonly IIBeamCurrentAccessControlService _access;
    private readonly IProjectRepository _projects;

    public ProjectService(
        IIBeamCurrentAccessControlService access,
        IProjectRepository projects)
    {
        _access = access;
        _projects = projects;
    }

    public async Task<bool> DeleteProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        await _access.RequirePermissionAsync("projects.delete", ct);
        await _access.RequireResourceAccessAsync("project", projectId, "manage", ct);

        return await _projects.DeleteAsync(projectId, ct);
    }
}
```

Attributes are available for discovery and optional policy/interceptor scenarios:

```csharp
using IBeam.Identity.Authorization;
using IBeam.Identity.Models;

public sealed class ProjectService
{
    [IBeamOperation(
        "projects.delete",
        Label = "Delete Project",
        Description = "Allows deleting projects.",
        Module = "products",
        ResourceType = "project",
        RequiredAccessLevel = AccessLevels.Manage,
        Category = "projects",
        IsDangerous = true)]
    [IBeamResourceAccess("project", "projectId", AccessLevels.Manage)]
    public Task<bool> DeleteProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        // Keep explicit checks here unless the host app opts into automatic enforcement.
        throw new NotImplementedException();
    }
}
```

Generic service bases can use templates with the resource registry:

```csharp
builder.Services.AddIBeamAccessControl(options =>
{
    options.Resources.Add<Project>(
        resourceKey: "project",
        permissionPrefix: "projects",
        label: "Project",
        module: "products");

    options.Resources.Add<Product>(
        resourceKey: "product",
        permissionPrefix: "products",
        label: "Product",
        module: "products");
});

public abstract class CrudService
{
    [IBeamOperationTemplate("{permissionPrefix}.delete", Operation = "delete", IsDangerous = true)]
    [IBeamResourceAccessTemplate("{resourceKey}", "id", AccessLevels.Manage)]
    public virtual Task DeleteAsync<T>(Guid id, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
```

The operation catalog endpoint exposes discovered operation metadata:

```http
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog/operations
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog/operations?module=products
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog/operations?resourceType=project
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog/operations?isDangerous=true
Authorization: Bearer {ownerOrAdminTenantToken}
```

Example item:

```json
{
  "key": "projects.delete",
  "label": "Delete Project",
  "description": "Allows deleting projects.",
  "moduleKey": "products",
  "resourceType": "project",
  "requiredAccessLevel": "manage",
  "category": "projects",
  "isAssignable": true,
  "isDangerous": true,
  "source": "attribute",
  "declaringType": "Hubbsly.Services.ProjectService",
  "methodName": "DeleteProjectAsync",
  "idParameter": "projectId"
}
```

Operation permissions also appear in the effective access catalog under `operations`, and the same permission mapping model assigns them to roles.

Controllers can use dynamic policies:

```csharp
[Authorize(Policy = "Permission:projects.delete")]
[Authorize(Policy = "Resource:project:manage")]
public Task<IActionResult> DeleteAsync(Guid projectId)
{
    throw new NotImplementedException();
}
```

`Resource:project:manage` resolves route values named `projectId`, `resourceId`, or `id`. Use `Resource:project:projectId:manage` to name the route parameter explicitly.

Unified access checks can require both operation permission and resource access:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/check
Authorization: Bearer {tenantScopedAccessToken}
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3",
  "permission": "projects.delete",
  "resourceType": "project",
  "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
  "minimumAccessLevel": "manage"
}
```

### Role and Permission Setup Flow

Create tenant roles:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/roles
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "name": "Work Viewer",
  "description": "Can view work items and related workspace information."
}
```

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/roles
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "name": "Product Manager",
  "description": "Can manage product records and coordinate related work."
}
```

Role responses include `description` so consuming UIs can explain tenant role access without duplicating copy. Descriptions are display/help metadata only; IBeam authorization continues to use role ids, role names, permission mappings, and access grants.

Grant roles to a user:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/roles/grant
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "userId": "be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3",
  "roleIds": [
    "8796f728-ee09-4c83-9c2f-086e03ff5624"
  ]
}
```

Map a permission to role names:

```http
PUT /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/permissions/mappings/by-name
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "permissionName": "work.view",
  "roleNames": ["Owner", "Administrator", "Work Viewer"],
  "roleIds": []
}
```

Map a permission to stable role IDs:

```http
PUT /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/permissions/mappings/by-id
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "permissionId": "11111111-2222-3333-4444-555555555555",
  "roleNames": [],
  "roleIds": [
    "8796f728-ee09-4c83-9c2f-086e03ff5624"
  ]
}
```

### Subject Grant Examples

Subject grants are generic so the model can support users, groups, teams, and API credentials. The current built-in subjects are strings such as `user` and `api-credential`.

Grant a module to a user:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/grants
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3",
  "resourceType": "module",
  "resourceId": "work",
  "accessLevel": "view"
}
```

Grant product edit access to a user:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/grants
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3",
  "resourceType": "product",
  "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
  "accessLevel": "edit"
}
```

List grants for a user:

```http
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/grants?subjectType=user&subjectId=be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3
Authorization: Bearer {ownerOrAdminTenantToken}
```

Update a grant:

```http
PUT /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/grants/7a89e64c-c8ec-40ee-9cd8-d315bde84a62
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3",
  "resourceType": "product",
  "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
  "accessLevel": "manage"
}
```

Delete a grant:

```http
DELETE /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/grants/7a89e64c-c8ec-40ee-9cd8-d315bde84a62
Authorization: Bearer {ownerOrAdminTenantToken}
```

### Current User Access Context

The frontend should use one boot-time endpoint to load the current access picture. It can drive navigation, admin visibility, module visibility, and read/edit/manage UI states from this response.

```http
GET /api/access/me
Authorization: Bearer {tenantScopedAccessToken}
```

Tenant-scoped equivalent:

```http
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/me
Authorization: Bearer {tenantScopedAccessToken}
```

Example response:

```json
{
  "userId": "be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3",
  "tenantId": "225925cc-995e-4584-a63b-4f2cb4f38f6f",
  "roles": ["Application"],
  "roleIds": ["8796f728-ee09-4c83-9c2f-086e03ff5624"],
  "permissions": ["work.view", "products.view"],
  "modules": [
    {
      "module": "work",
      "accessLevel": "view",
      "source": "grant"
    }
  ],
  "resources": {
    "product": [
      {
        "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
        "accessLevel": "edit",
        "source": "grant"
      }
    ]
  },
  "capabilities": {
    "canManageUsers": false,
    "canManageRoles": false,
    "canManageAccess": false,
    "canAssignOwner": false
  }
}
```

### Access Check Endpoint

Use the check endpoint for admin screens or thin API wrappers that need to ask whether a subject has a grant to a resource. The route requires a tenant member token; normal administration should use an owner/admin token.

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/check
Authorization: Bearer {tenantScopedAccessToken}
Content-Type: application/json

{
  "subjectType": "user",
  "subjectId": "be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3",
  "resourceType": "product",
  "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
  "accessLevel": "edit"
}
```

Example response:

```json
{
  "isAllowed": true,
  "source": "grant",
  "reason": null,
  "accessLevel": "edit"
}
```

### API Credential Grants

API credentials remain separate from normal human role assignment UI. A host can grant access to an API credential by using `subjectType = "api-credential"` and the credential ID as `subjectId`.

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/grants
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "subjectType": "api-credential",
  "subjectId": "1dff7a6a-545a-4790-aec1-37f5c5182fb1",
  "resourceType": "module",
  "resourceId": "work",
  "accessLevel": "manage"
}
```

### Authorization Policies

Access-control policies are dynamic strings. They can be used directly with ASP.NET Core authorization.

```csharp
[Authorize(Policy = "RequirePermission:users.manage")]
public sealed class TenantUsersController : ControllerBase
{
    [HttpPost("invite")]
    public IActionResult Invite() => Ok();
}
```

```csharp
[Authorize(Policy = "RequireModule:work:view")]
public sealed class WorkController : ControllerBase
{
    [HttpGet]
    public IActionResult GetBoard() => Ok();
}
```

```csharp
[Authorize(Policy = "RequireResource:project:24e4785d-d558-4511-a879-b70d5c88cd51:edit")]
public IActionResult EditProject() => Ok();
```

For resource-specific policy strings, prefer imperative checks when the resource ID comes from the route:

```csharp
public sealed class ProjectsController : ControllerBase
{
    private readonly IIBeamAccessControlService _access;

    public ProjectsController(IIBeamAccessControlService access)
    {
        _access = access;
    }

    [HttpPut("/api/projects/{projectId:guid}")]
    public async Task<IActionResult> Update(Guid projectId, UpdateProjectRequest request, CancellationToken ct)
    {
        await _access.RequireResourceAccessAsync(
            User,
            resourceType: "project",
            resourceId: projectId.ToString("D"),
            minimumAccessLevel: "edit",
            ct);

        return Ok();
    }
}
```

### Host Override Rules

`IIBeamAccessRuleProvider` lets a host add app-specific rules that cannot be represented as a static role, permission, or grant. For example, a project owner in the app domain may receive implicit `manage` access to the project.

```csharp
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

public sealed class ProjectOwnerAccessRuleProvider : IIBeamAccessRuleProvider
{
    private readonly IProjectRepository _projects;

    public ProjectOwnerAccessRuleProvider(IProjectRepository projects)
    {
        _projects = projects;
    }

    public async Task<IReadOnlyList<AccessDecision>> EvaluateAsync(
        AccessEvaluationContext context,
        CancellationToken ct = default)
    {
        if (!string.Equals(context.ResourceType, "project", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<AccessDecision>();

        if (!Guid.TryParse(context.SubjectId, out var userId))
            return Array.Empty<AccessDecision>();

        if (!Guid.TryParse(context.ResourceId, out var projectId))
            return Array.Empty<AccessDecision>();

        var isOwner = await _projects.IsProjectOwnerAsync(
            context.TenantId,
            projectId,
            userId,
            ct);

        return isOwner
            ? [new AccessDecision(true, "host-rule", "Project owner.", "manage")]
            : Array.Empty<AccessDecision>();
    }
}
```

Register host rule providers directly with DI:

```csharp
builder.Services.AddScoped<IIBeamAccessRuleProvider, ProjectOwnerAccessRuleProvider>();
```

### Recommended Migration Shape

For an app such as Hubbsly:

1. Register modules and permission catalog entries in IBeam.
2. Move role-permission assignments to IBeam permission mappings.
3. Replace app-owned user/resource grant endpoints with `/access-control/grants`.
4. Replace app-owned access catalog controllers with `IIBeamAccessCatalogProvider`.
5. Replace frontend boot-time grant wrappers with `GET /api/access/me`.
6. Keep product/project/work domain tables and enforce domain-specific rules in services.

## Attribute Examples

```csharp
[AllowRoles("owner", "admin")]
public sealed class PatientController : ControllerBase
{
    [HttpPost("save")]
    [AllowRoleIds("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9")]
    public IActionResult Save() => Ok();
}
```

`AllowRoles` uses built-in ASP.NET Core role authorization against the `role` claim type.  
`AllowRoleIds` uses a dynamic policy that checks `rid` (or `role_id`) claims.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Azure Table schema inventory: [`../docs/identity-azure-table-schema-inventory.md`](../docs/identity-azure-table-schema-inventory.md)
- Roles, permissions, and grants: [`../docs/roles-permissions-and-grants.md`](../docs/roles-permissions-and-grants.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents should keep controllers thin. Identity API controllers should expose service behavior, not duplicate service-layer auth, tenant, role, grant, or audit rules.
