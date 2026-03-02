# IBeam.Identity.Services

`IBeam.Identity.Services` contains the core authentication orchestration and token logic used by the API.

## What this project does

- Implements auth services:
  - `PasswordAuthService`
  - `OtpAuthService`
  - `OAuthAuthService`
- Implements OTP challenge lifecycle via `OtpService`.
- Implements JWT + refresh token rotation + session operations in `JwtTokenService`.
- Provides DI extensions for registering identity services.

## Service registration

Use:

- `AddIBeamIdentityServices(configuration)` for core services/options
- `AddIBeamIdentityAuthPasswordService()`
- `AddIBeamIdentityAuthOtpService()`
- `AddIBeamIdentityAuthOAuthService()`

Note: store interfaces (`IIdentityUserStore`, `IOtpChallengeStore`, `IAuthSessionStore`, etc.) must be provided by a repository project.

## Required configuration

### `IBeam:Identity:Jwt`

- `Issuer`
- `Audience`
- `SigningKey`
- `AccessTokenMinutes`
- `PreTenantTokenMinutes`
- `RefreshTokenDays`
- `ClockSkewSeconds`
- `KeyId` (optional)

### `IBeam:Identity:Otp`

- `CodeLength`
- `ExpirationMinutes`
- `MaxAttempts`
- `HashSalt`
- `VerificationTokenSecret`
- `VerificationTokenMinutes`

### `IBeam:Identity:Features`

- `Otp`
- `PasswordAuth`
- `TwoFactor`
- `OAuth`
- `TenantSelection`
- `ClaimsEnrichment`

### `IBeam:Identity:OAuth`

- `StateTtlMinutes`
- `Providers:{providerName}` entries with:
  - `Enabled`
  - `ClientId`
  - `ClientSecret`
  - `AuthorizationEndpoint`
  - `TokenEndpoint`
  - `UserInfoEndpoint`
  - `Scope`

## Build

```bash
dotnet restore
dotnet build
```
