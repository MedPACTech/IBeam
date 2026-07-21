# IBeam
[![CI](https://github.com/MedPACTech/IBeam/actions/workflows/ci.yml/badge.svg)](https://github.com/MedPACTech/IBeam/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/IBeam.Services)](https://www.nuget.org/packages/IBeam.Services)
[![NuGet Downloads](https://img.shields.io/nuget/dt/IBeam.Services)](https://www.nuget.org/packages/IBeam.Services)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/MedPACTech/IBeam)](https://github.com/MedPACTech/IBeam/releases)

IBeam is a modular .NET framework for teams that want to move from idea to working API quickly, without giving up architecture quality.

The framework provides a strong base API and reusable application patterns so developers can stand up production-ready services with minimal implementation code. From there, teams can compose only the modules they need, extend where they want full control, and avoid rewriting the same plumbing in every new project.

## Why IBeam Exists
Most teams repeatedly solve the same cross-cutting concerns before they can deliver product value:
- service orchestration,
- data access patterns,
- identity flows,
- communication providers,
- logging and auditing,
- storage integration,
- API consistency.

IBeam packages these concerns into composable building blocks.

Developers can use IBeam on its own, alongside existing libraries, or with AI-assisted workflows to rapidly scaffold and evolve APIs. The goal is not to lock teams into one way of building. The goal is to provide a stable foundation and extension points so teams can customize intelligently.

## Design Goals
- Fast startup: get a base API running with very little custom code.
- Modular by default: add only the packages you need.
- Extension-first: override behavior with your own services/providers.
- Provider flexibility: choose storage, messaging, and identity integrations per project.
- Production mindset: testing, configuration, and operational patterns are first-class.

## Open-Core and Open Source Commitment
IBeam uses an open-core model:
- Core framework: Apache-2.0 open source
- Enterprise add-ons: commercial terms for premium/enterprise modules

This allows individuals and small teams to build freely while supporting long-term sustainability for enterprise-scale usage.

See:
- `LICENSE`
- `LICENSE-COMMERCIAL.md`
- `docs/licensing.md`

## Forking and Community Contributions
Forking is welcome for the Apache-2.0 core.

Contributions are encouraged across:
- bug fixes,
- new extension packages,
- documentation quality,
- integration examples,
- test coverage.

See `CONTRIBUTING.md` for workflow and standards.

## Community and Security
- Code of Conduct: `CODE_OF_CONDUCT.md`
- Security Policy: `SECURITY.md`

## Package Map

### API
- `IBeam.Api`: reusable API composition helpers (response envelopes, exception middleware, DI/config builder)
- `IBeam.Identity.Api`: identity endpoint module that composes identity services + providers

### Access Control
- `IBeam.AccessControl`: core access-control contracts and models
- `IBeam.AccessControl.Services`: grants, permission-role maps, service-operation rules, and access evaluation services
- `IBeam.AccessControl.Api`: optional ASP.NET Core endpoints for dynamic access-control management
- `IBeam.AccessControl.Repositories.AzureTable`: Azure Table-backed access-control stores

### AI
- `IBeam.Ai`: core agent tooling contracts and MCP models
- `IBeam.Ai.Services`: agent tool registry, authorization, and MCP service orchestration
- `IBeam.Ai.Api`: ASP.NET Core endpoint wiring for IBeam AI agent and MCP tooling

IBeam NuGet packages include package-specific AI guidance under `.agent/prompt.md`.
To make that guidance available to an AI coding agent in an application that already uses
an `.agent` directory, add this opt-in property to the application project or its
`Directory.Build.props`:

```xml
<IBeamEnableAgentPrompts>true</IBeamEnableAgentPrompts>
```

The package prompt is then copied, only when missing, to
`.agent/packages/<package-id>/prompt.md`. Existing application and package prompts are
not overwritten.

For repository work, agents should start with the root guide at [`.agent/implementation-guide.md`](.agent/implementation-guide.md), then read the package README and `.agent/prompt.md` for each package being used. Extended docs include:

- [service logging and audit](docs/service-logging-and-audit.md)
- [service operation permissions](docs/service-operation-permissions.md)
- [roles, permissions, and grants](docs/roles-permissions-and-grants.md)
- [identity Azure Table schema inventory](docs/identity-azure-table-schema-inventory.md)
- [consuming API migration prompt](IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)
- [IBeam 2.8 consuming API upgrade prompt](IBeam.AI.Enablement/examples/ibeam-2.8-consuming-api-upgrade-prompt.md)

### Licensing
- `IBeam.Licensing`: core tenant licensing contracts and models
- `IBeam.Licensing.Services`: plan catalog, tenant license, seat assignment, and entitlement services
- `IBeam.Licensing.Api`: ASP.NET Core endpoint wiring for tenant application licensing

Quick start:

```powershell
dotnet add package IBeam.Licensing.Api
```

```csharp
builder.Services.AddIBeamLicensingApi(builder.Configuration);
app.MapIBeamLicensing();
```

Configure plans under `IBeam:Licensing:Plans`, then use `ILicenseAuthorizer` or the licensing API endpoints to check tenant entitlements. See the package READMEs for full setup, API examples, and production store replacement guidance.

### Communications
- `IBeam.Communications`: provider-agnostic email/SMS contracts, options, validation, templating orchestration
- `IBeam.Communications.Email.Templating`: file-based email template renderer and templated send orchestration
- `IBeam.Communications.Email.AzureCommunications`: Azure Communication Services email provider
- `IBeam.Communications.Email.SendGrid`: SendGrid email provider
- `IBeam.Communications.Email.Smtp`: SMTP email provider
- `IBeam.Communications.Email.PickupDirectory`: local filesystem email pickup provider
- `IBeam.Communications.Sms.AzureCommunications`: Azure Communication Services SMS provider
- `IBeam.Communications.Sms.Twilio`: reserved package for Twilio SMS provider (currently scaffold state)

### Identity
- `IBeam.Identity`: identity contracts, models, options, events, and schema abstractions
- `IBeam.Identity.Services`: identity orchestration (OTP, password, OAuth, tokens, tenant selection, tenant invitations)
- `IBeam.Identity.Repositories.AzureTable`: Azure Table-backed identity stores and schema bootstrap
- `IBeam.Identity.Repositories.EntityFramework`: EF-backed identity store wiring (Sqlite currently active)

## Unified Roles, Permissions, and Access Grants

IBeam Identity can act as the source of truth for tenant roles, permission mappings, subject grants, module access, resource access, and the evaluated current-user access context.

Host applications provide app-specific catalogs:

- module keys such as `work`, `products`, `planning`, and `money`
- permission names such as `users.manage`, `work.view`, and `products.edit`
- resource types such as `product`, `project`, `contact`, and `record`
- dynamic catalog providers that expose tenant resources to IBeam

IBeam provides the shared model and endpoints:

```csharp
builder.Services.AddIBeamIdentityApi(builder.Configuration);
builder.Services.AddIBeamAccessControl(options =>
{
    options.Modules.AddRange(HubbslyModules.All);
    options.ResourceCatalogProviders.Add<HubbslyAccessCatalogProvider>();
});
builder.Services.AddIBeamIdentityApiControllers();

app.MapIBeamAccessControlApi();
```

Core access-control endpoints:

```http
GET  /api/tenants/{tenantId}/permissions/catalog
GET  /api/tenants/{tenantId}/permissions/mappings
PUT  /api/tenants/{tenantId}/permissions/mappings/by-name

GET  /api/tenants/{tenantId}/access-catalog
GET  /api/tenants/{tenantId}/access-control/grants?subjectType=user&subjectId={userId}
POST /api/tenants/{tenantId}/access-control/grants
POST /api/tenants/{tenantId}/access-control/check
GET  /api/access/me
GET  /api/tenants/{tenantId}/access-control/me
```

Example grant:

```json
{
  "subjectType": "user",
  "subjectId": "be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3",
  "resourceType": "product",
  "resourceId": "24e4785d-d558-4511-a879-b70d5c88cd51",
  "accessLevel": "edit"
}
```

Services can enforce access without duplicating app-owned role-permission tables:

```csharp
await access.RequireResourceAccessAsync(
    User,
    resourceType: "project",
    resourceId: projectId.ToString("D"),
    minimumAccessLevel: "edit",
    ct);
```

See `IBeam.Identity.Api/README.md` for detailed setup, HTTP examples, current-user access context examples, API credential grants, dynamic resource catalog providers, and authorization policy examples.

## Tenant Roles and API Credentials

IBeam Identity supports tenant-scoped roles and tenant-level API credentials. API credentials are
service identities, not human users. They belong to a tenant, store only a hashed secret, emit a
credential principal, and can be assigned tenant role IDs and/or API-safe role/scope names.
Tenant roles can also carry optional descriptions so consuming UIs can explain access levels from
the same role-list/detail calls used for assignment.

Role management endpoints:

```http
GET    /api/tenants/{tenantId}/roles
GET    /api/tenants/{tenantId}/roles/{roleId}
POST   /api/tenants/{tenantId}/roles
PUT    /api/tenants/{tenantId}/roles/{roleId}
DELETE /api/tenants/{tenantId}/roles/{roleId}
```

User role assignment endpoints:

```http
GET  /api/tenants/{tenantId}/users/{userId}/roles
POST /api/tenants/{tenantId}/roles/grant
POST /api/tenants/{tenantId}/roles/revoke
```

Use the tenant role endpoints as the canonical role catalog. The access catalog is intentionally focused on fine-grained permissions, operations, modules, resources, tools, agents, API scopes, and access levels; tenant roles are not part of the access-catalog contract.

Tenant invite endpoints let owners/admins invite by email or SMS, preserve tenant context, apply roles and optional access grants, and ensure host-owned user extension rows during acceptance:

```http
POST /api/tenants/{tenantId}/invites
GET  /api/tenants/{tenantId}/invites
GET  /api/tenants/{tenantId}/invites/{inviteId}
POST /api/tenants/{tenantId}/invites/{inviteId}/resend
POST /api/tenants/{tenantId}/invites/{inviteId}/revoke
GET  /api/invites/{tokenOrCode}/preview
POST /api/invites/accept
```

Management access for tenant, invite, role, permission mapping, access-control, auth-attempt, and API credential endpoints is configurable through `IBeam:Identity:AccessControl`. Defaults preserve the built-in `Owner`, `Administrator`, and `Admin` role behavior, while larger apps can configure their own admin tiers and operation-style permission names such as `identity.tenantinvites.manage` or `identity.apicredentials.manage`.

Azure Table identity storage uses these default table names for tenant membership:

```text
Tenants
TenantUsers
UserTenants
Roles
TenantInvites
ApiCredentials
```

Existing applications that already use a `TenantRoles` table can keep that physical table by setting
`IBeam:Identity:AzureTable:TenantRolesTableName` to `TenantRoles`.

API credential management endpoints:

```http
GET  /api/api-credentials
GET  /api/api-credentials/role-catalog
POST /api/api-credentials
PUT  /api/api-credentials/{credentialId}/roles
POST /api/api-credentials/{credentialId}/revoke
POST /api/api-credentials/introspect
```

Create request:

```json
{
  "displayName": "Marketing App Email Worker",
  "agentKey": "marketing",
  "roleNames": ["API", "api-scope:email", "email:send"],
  "roleIds": [],
  "expiresUtc": "2026-09-20T00:00:00Z"
}
```

The raw `apiKey` is returned only from the create response. Store it in the calling system's secret
store. IBeam stores only the secure hash.

The default raw key prefix is `ibk`, producing keys that start with `ibk_`. Applications can configure
their own alphanumeric prefix for newly created credentials:

```json
{
  "IBeam": {
    "Identity": {
      "ApiCredentials": {
        "KeyPrefix": "hbk"
      }
    }
  }
}
```

API credential role names are service/agent scopes assigned directly to an API credential. They are
separate from human tenant membership roles such as `Owner`, `Administrator`, or `Admin`.
Use `GET /api/api-credentials/role-catalog` to drive admin UI assignment lists. The built-in catalog
includes:

```text
API
tool:mcp
api-scope:*
api-scope:work
api-scope:contacts
api-scope:money
agent:*
```

Host apps can add app-specific catalog entries without forking IBeam:

```json
{
  "IBeam": {
    "Identity": {
      "ApiCredentials": {
        "RoleCatalog": [
          {
            "Name": "api-scope:calendar",
            "DisplayName": "Calendar",
            "Description": "Allows access to Calendar API and MCP tools.",
            "Category": "module"
          }
        ]
      }
    }
  }
}
```

API credential authentication accepts either:

```http
X-API-Key: {raw-api-key}
Authorization: ApiKey {raw-api-key}
```

Successful API-key authentication emits:

```text
tenant_id / tid
sub / nameidentifier / uid
api_credential_id
api_subject_type = credential
api_agent_key / agent_key
role claims
name
rid / role_id claims
```

Role-rule options for services and APIs:

- Use built-in ASP.NET authorization with role names, for example `[Authorize(Roles = "API,email:send")]`.
- Use IBeam role attributes on service methods, for example `[RoleAccess("API", "email:send")]`.
- Use IBeam role-id attributes when role IDs are stable, for example `[RoleAccessId("{roleId}")]`.
- Use `IRoleAccessAuthorizer` in service code when authorization must be checked imperatively.
- Use `IPermissionAccessAuthorizer` and permission mappings when a method should be protected by a permission that maps to tenant roles.
- Use API credential role/scope names such as `api-scope:email`, `email:send`, `agent:marketing`, or `project:hubbsly` for service-safe authorization.
- Keep human-management roles such as `Owner`, `Administrator`, and `Admin` off API credentials unless a host app explicitly replaces `IApiCredentialRoleAssignmentValidator`.
- For tool/MCP-style APIs, protect the tool endpoint with API-key auth and require the needed service role/scope on the action.
- For distributed services that cannot access the credential store directly, use `POST /api/api-credentials/introspect` from a trusted internal human/admin or service context.

### Data and Services
- `IBeam.Repositories`: repository abstractions and base implementations
- `IBeam.Repositories.AzureTables`: Azure Table repository implementation
- `IBeam.Repositories.OrmLite`: ServiceStack OrmLite repository implementation
- `IBeam.Services`: service abstractions + base service implementations and operation policy resolver
- `IBeam.Services.AutoMapper`: `IModelMapper<TEntity,TModel>` bridge powered by AutoMapper
- `IBeam.Services.Logging`: optional service auditing/logging sinks and actor providers

### Storage
- `IBeam.Storage.Abstractions`: common blob storage contracts
- `IBeam.Storage.AzureBlobs`: Azure Blob Storage implementation
- `IBeam.Storage.FileSystem`: local and mounted filesystem blob implementation
- `IBeam.Storage.S3`: S3-compatible blob storage implementation

### Utilities
- `IBeam.Utilities`: shared utility primitives (auditing, exception middleware, cache and token helpers)

## Build

```bash
dotnet restore
dotnet build IBeam.sln
dotnet test IBeam.sln
```

## Roadmap Note
A public landing page is planned. See `docs/landing-page-plan.md`.
