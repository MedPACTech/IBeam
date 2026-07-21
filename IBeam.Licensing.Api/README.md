# IBeam.Licensing.Api

`IBeam.Licensing.Api` provides optional ASP.NET Core endpoint wiring for IBeam tenant application licensing.

```powershell
dotnet add package IBeam.Licensing.Api
```

## When To Use This

- You want HTTP endpoints for plan lookup, tenant license management, seat assignment, and entitlement checks.
- You want admins or internal tools to manage tenant licensing dynamically.
- You want thin API endpoints that delegate licensing rules to `IBeam.Licensing.Services`.

Do not expose this package without host authentication and authorization. License administration is a privileged surface.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Endpoint mapping | `MapIBeamLicensing(...)` | Adds licensing route groups to an ASP.NET Core app. |
| Registration helper | `AddIBeamLicensingApi(...)` | Registers licensing services needed by the endpoint layer. |
| Plan endpoints | Minimal API handlers | Lists configured license plans. |
| Tenant license endpoints | Minimal API handlers | Grants, updates, lists, and revokes tenant licenses. |
| Seat endpoints | Minimal API handlers | Assigns and revokes license seats. |
| Entitlement endpoints | Minimal API handlers | Lists/checks tenant entitlements. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is API-only. Endpoints bind request data, call licensing services, and return responses. Business rules, entitlement checks, and audit boundaries belong in the service layer.

## Quick Start

```csharp
using IBeam.Licensing.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddIBeamLicensingApi(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapIBeamLicensing("/api", "TenantAdmin");

app.Run();
```

## Endpoint Overview

```http
GET    /api/license-plans
GET    /api/tenants/{tenantId}/licenses
POST   /api/tenants/{tenantId}/licenses
PUT    /api/tenants/{tenantId}/licenses/{licenseId}
POST   /api/tenants/{tenantId}/licenses/{licenseId}/revoke
GET    /api/tenants/{tenantId}/licenses/{licenseId}/assignments
POST   /api/tenants/{tenantId}/licenses/{licenseId}/assignments
DELETE /api/tenants/{tenantId}/licenses/{licenseId}/assignments/{assignmentId}
GET    /api/tenants/{tenantId}/license-entitlements
POST   /api/tenants/{tenantId}/license-entitlements/check
```

Example license grant:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/licenses
Content-Type: application/json

{
  "planKey": "hubbsly-work",
  "providerName": "stripe",
  "providerCustomerId": "cus_123",
  "providerSubscriptionId": "sub_123"
}
```

Example seat assignment:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/licenses/{licenseId}/assignments
Content-Type: application/json

{
  "subject": {
    "subjectType": "agent",
    "subjectId": "codex",
    "displayName": "Codex"
  }
}
```

Example entitlement check:

```http
POST /api/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/license-entitlements/check
Content-Type: application/json

{
  "subject": {
    "subjectType": "agent",
    "subjectId": "codex"
  },
  "entitlement": "work:cards:create"
}
```

## Configuration

```json
{
  "IBeam": {
    "Licensing": {
      "Plans": [
        {
          "Key": "hubbsly-work",
          "DisplayName": "Hubbsly Work",
          "Entitlements": [ "feature:work", "work:cards:create", "mcp:tools" ],
          "Limits": {
            "Seats": 4,
            "McpCallsPerMonth": 10000
          }
        }
      ]
    }
  }
}
```

## Data Storage

This API package does not create tables or repositories. It uses the store configured by `IBeam.Licensing.Services`. The default services package registers an in-memory store; production hosts should replace `ILicensingStore`.

## Service Operations, Auditing, And Permissions

Licensing endpoints do not replace Identity authorization. Host APIs should authenticate the user, API credential, or agent and check tenant roles/API scopes before allowing license administration.

The underlying services expose operation tags such as `licensing.licenses.grant` and `licensing.seats.assign`, so IBeam service policies and audit logging can be applied at the service boundary.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents should keep this package thin and put new licensing behavior in services.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Endpoints return 401/403 | Host authentication or policy is not satisfied | Verify `UseAuthentication`, `UseAuthorization`, and the mapping policy. |
| License changes disappear | Default in-memory store is active | Register a persistent `ILicensingStore`. |
| Entitlement check fails unexpectedly | Plan key, entitlement name, license status, or seat is wrong | Inspect configured plans, active licenses, and assignments. |

## Version Notes

- Targets `net10.0`.
- Built for optional runtime licensing management.
- Package version is assigned by the repository release workflow.
