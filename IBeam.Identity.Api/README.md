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
