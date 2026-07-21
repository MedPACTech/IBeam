# IBeam.AccessControl.Api

`IBeam.AccessControl.Api` provides optional ASP.NET Core endpoint wiring for managing IBeam access-control grants, permission-role maps, and service-operation permission rules over HTTP. It is for teams that want runtime administration APIs instead of configuration-only or script-only management.

## When To Use This

- You want admins or internal tools to manage resource grants dynamically.
- You want API endpoints for permission-to-role mappings.
- You want API endpoints for service-operation allow/deny rules.
- You are comfortable exposing these capabilities behind a strong authorization policy.

Do not install this package if access-control rules should only be changed through configuration, migrations, or locked-down scripts.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Endpoint mapping | `MapIBeamAccessControl(...)` | Adds access-control route groups to an ASP.NET Core app. |
| Service registration helper | `AddIBeamAccessControlServices(...)` usage | Runtime services are registered from the services package. |
| Permission-map endpoints | Minimal API handlers | Manage role grants by permission name or permission ID. |
| Service-operation endpoints | Minimal API handlers | Manage operation allow/deny rules and check operation access. |
| Resource-grant endpoints | Minimal API handlers | Manage resource grants and resource access checks. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is API-only. Endpoints should stay thin: bind route/body data, call one access-control service method, and return the result. Authorization rules, permission evaluation, validation, and persistence belong in `IBeam.AccessControl.Services` and repository packages.

## Quick Start

```csharp
using IBeam.AccessControl.Api;
using IBeam.AccessControl.Services;

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddIBeamAccessControlServices(builder.Configuration);
builder.Services.AddIBeamServiceOperationPermissionManagement();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapIBeamAccessControl("/api", authorizationPolicy: "AccessControlAdmin");
```

## Endpoint Overview

Default route prefix:

```csharp
app.MapIBeamAccessControl("/api", authorizationPolicy: "AccessControlAdmin");
```

Representative endpoints:

```http
GET    /api/tenants/{tenantId}/access-control/grants
POST   /api/tenants/{tenantId}/access-control/grants
PUT    /api/tenants/{tenantId}/access-control/grants/{grantId}
DELETE /api/tenants/{tenantId}/access-control/grants/{grantId}
POST   /api/tenants/{tenantId}/access-control/grants/check

GET    /api/tenants/{tenantId}/access-control/permission-maps
PUT    /api/tenants/{tenantId}/access-control/permission-maps/by-name/{permissionName}
PUT    /api/tenants/{tenantId}/access-control/permission-maps/by-id/{permissionId}
DELETE /api/tenants/{tenantId}/access-control/permission-maps/by-name/{permissionName}
DELETE /api/tenants/{tenantId}/access-control/permission-maps/by-id/{permissionId}

GET    /api/tenants/{tenantId}/access-control/service-permissions
POST   /api/tenants/{tenantId}/access-control/service-permissions
PUT    /api/tenants/{tenantId}/access-control/service-permissions/{ruleId}
POST   /api/tenants/{tenantId}/access-control/service-permissions/{ruleId}/disable
DELETE /api/tenants/{tenantId}/access-control/service-permissions/{ruleId}
POST   /api/tenants/{tenantId}/access-control/service-permissions/check
```

Grant list endpoints return active, unexpired grants by default. Add `includeRevoked=true` or `includeInactive=true` to include revoked, disabled, or expired grants for admin history views. Deleting a grant soft-revokes it and returns `204 No Content`.

Example operation rule request:

```json
{
  "pattern": "pricing.*",
  "effect": "allow",
  "subjectTypes": [ "user" ],
  "roleNames": [ "Accounting" ],
  "roleIds": [],
  "priority": 0
}
```

## Configuration

This package primarily depends on service-layer configuration.

| Section | Purpose |
|---|---|
| `IBeam:AccessControl` | Resource access options. |
| `IBeam:Services:Authorization` | Service-operation allow/deny rules. |
| `IBeam:AccessControl:AzureTable` | Persistent store configuration when Azure Table repositories are installed. |

## Service Operations, Auditing, And Permissions

The API package does not implement authorization rules itself. It should be protected by host ASP.NET Core authorization:

```csharp
app.MapIBeamAccessControl("/api", authorizationPolicy: "AccessControlAdmin");
```

The services called by these endpoints are operation-tagged and audited by `IBeam.AccessControl.Services` when IBeam service auditing is enabled.

## Data Storage

This API package does not create or own storage. It calls service/store abstractions.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| Access-control tables | No | Use `IBeam.AccessControl.Repositories.AzureTable` or a custom store. |
| In-memory stores | No | Registered by `IBeam.AccessControl.Services`. |
| API request logs | No | Use host logging/middleware. |

For Azure Table schema, see [`../IBeam.AccessControl.Repositories.AzureTable/README.md`](../IBeam.AccessControl.Repositories.AzureTable/README.md).

## Extension Points

| Extension Point | Interface/Hook | Why Replace It |
|---|---|---|
| Route prefix | `MapIBeamAccessControl(prefix, policy)` | Mount under a host-specific API prefix. |
| Authorization policy | ASP.NET Core policy name | Lock management endpoints to owners/admins. |
| Stores | access-control store interfaces | Persist dynamic grants and rules. |
| API surface | host endpoint composition | Skip this package and manage rules through scripts/config if dynamic APIs are not desired. |

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Roles, permissions, and grants: [`../docs/roles-permissions-and-grants.md`](../docs/roles-permissions-and-grants.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents should preserve the thin API pattern and keep permission logic in the service layer.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Endpoints return 401/403 | Host auth/policy not satisfied | Verify authentication and the mapping policy. |
| Dynamic updates do not persist | In-memory stores are active | Register a persistent repository package after services. |
| Operation check always denies | No matching rule or wrong operation name | Verify service method `[IBeamOperation]` names and configured patterns. |
| API package feels too open | Runtime management is optional | Remove this package and manage access-control through config/scripts. |

## Version Notes

- Targets `net10.0`.
- Built for optional dynamic management.
- Package version is assigned by the repository release workflow.
