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

Role management endpoints require an authenticated tenant token (`tid`) with one of these role claims: `owner`, `administrator`, or `admin`.

## API Credential Role Catalog

API credential role names are machine/agent scopes assigned directly to API credentials. They are
separate from tenant user membership roles.

- `GET /api/api-credentials/role-catalog`

The catalog endpoint returns structured entries with name, display name, description, category,
and built-in/pattern/assignable flags. Built-ins include `API`, `tool:mcp`, `api-scope:*`,
`api-scope:work`, `api-scope:contacts`, `api-scope:money`, and `agent:*`.

Configured host entries can be added under `IBeam:Identity:ApiCredentials:RoleCatalog`.

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
            SupportedAccessLevels: ["view", "edit", "manage"])));

        return resources;
    }
}
```

The access catalog endpoint merges static modules with dynamic provider resources:

```http
GET /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-catalog
Authorization: Bearer {ownerOrAdminTenantToken}
```

Example response:

```json
{
  "resourceTypes": ["module", "product", "project"],
  "accessLevels": ["view", "edit", "manage"],
  "resources": [
    {
      "resourceType": "module",
      "resourceId": "work",
      "label": "Work",
      "description": "Work board access.",
      "supportedAccessLevels": ["view", "edit", "manage"]
    },
    {
      "resourceType": "product",
      "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
      "label": "Qurvia",
      "description": "Remote patient monitoring product.",
      "supportedAccessLevels": ["view", "edit", "manage"]
    }
  ]
}
```

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

### Role and Permission Setup Flow

Create tenant roles:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/roles
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{ "name": "Work Viewer" }
```

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/roles
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{ "name": "Product Manager" }
```

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

Subject grants are generic so the model can support users, groups, teams, and API credentials. The current built-in subjects are strings such as `user` and `apiCredential`.

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

API credentials remain separate from normal human role assignment UI. A host can grant access to an API credential by using `subjectType = "apiCredential"` and the credential ID as `subjectId`.

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/access-control/grants
Authorization: Bearer {ownerOrAdminTenantToken}
Content-Type: application/json

{
  "subjectType": "apiCredential",
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
