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
builder.Services.AddIBeamIdentityApiControllers();
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
