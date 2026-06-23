# IBeam.Licensing.Api

ASP.NET Core endpoint wiring for IBeam tenant application licensing.

Install:

```powershell
dotnet add package IBeam.Licensing.Api
```

Register services:

```csharp
builder.Services.AddIBeamLicensingApi(builder.Configuration);
```

Map endpoints:

```csharp
app.MapIBeamLicensing();
```

The endpoint group requires authorization by default.

Complete minimal setup:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddIBeamLicensingApi(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapIBeamLicensing();

app.Run();
```

Default endpoints:

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

Use a custom route prefix or authorization policy when mapping:

```csharp
app.MapIBeamLicensing("/api", "TenantAdmin");
```

Example plan configuration:

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

Example API flow:

```http
GET /api/license-plans
```

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

Licensing endpoints do not replace Identity authorization. Host APIs should still authenticate the user, API credential, or agent and check tenant roles/API scopes before granting or consuming licensed capabilities.
