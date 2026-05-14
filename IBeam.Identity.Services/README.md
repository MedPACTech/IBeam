# IBeam.Identity.Services

Core identity orchestration package for OTP, password, OAuth, tokens, and tenant selection.

## Narrative Introduction

This package is where identity behavior is implemented. It consumes contracts from `IBeam.Identity` and composes authentication workflows while delegating storage and delivery concerns to repository and communications providers.

## Features and Components

- auth flow implementations:
  - `PasswordAuthService`
  - `OtpAuthService`
  - `OAuthAuthService`
- supporting services:
  - `OtpService`
  - `JwtTokenService`
  - `TenantSelectionService`
  - `IdentityCommunicationAdapter`
  - `PermissionAccessAuthorizer` (dynamic permission map authorization)
  - `PermissionCatalogProvider` (exposed permission catalog discovery)
- DI extension methods:
  - `AddIBeamIdentityServices(IConfiguration)`
  - `AddIBeamIdentityPermissionMappings(...)`
  - `AddIBeamIdentityPermissionCatalog(...)`
  - `AddIBeamIdentityAuthPasswordService()`
  - `AddIBeamIdentityAuthOtpService()`
  - `AddIBeamIdentityAuthOAuthService()`
  - `AddIBeamAuthEvents(...)`

## Dependencies

- Internal packages:
  - `IBeam.Identity`
  - `IBeam.Communications`
- External packages:
  - `Microsoft.Extensions.Configuration.Abstractions`
  - `Microsoft.Extensions.Caching.Abstractions`
  - `Microsoft.Extensions.Http`
  - `Microsoft.Extensions.Options`
  - `Microsoft.Extensions.Options.ConfigurationExtensions`
  - `Microsoft.Extensions.Identity.Stores`
  - `System.IdentityModel.Tokens.Jwt`

## Required Configuration

- `IBeam:Identity:Jwt`
- `IBeam:Identity:Otp`
- `IBeam:Identity:Features`
- `IBeam:Identity:OAuth` (when OAuth is enabled)
- `IBeam:Identity:Events` (optional)
- `IBeam:Identity:PermissionAccess` (optional; JSON permission map source)
- `IBeam:Identity:RoleManagement` (optional; tenant/admin policy toggles)

### OTP Auto-Provision Toggle

- `IBeam:Identity:Otp:AllowAutoProvisionForUnknownUser`
  - `true`: OTP sign-in may create users for unknown destinations
  - `false`: unknown destinations are blocked in OTP start/complete flows
- Default when omitted:
  - `Development`: `true`
  - `Test` / `Production`: `false`
- Environment-variable override:
  - `IBeam__Identity__Otp__AllowAutoProvisionForUnknownUser=true|false`
