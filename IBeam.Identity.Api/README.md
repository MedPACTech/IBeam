# IBeam.Identity.Api

`IBeam.Identity.Api` is the HTTP host for Identity authentication and account APIs.

## What this project does

- Hosts REST endpoints for:
  - OTP start/complete
  - Email/password registration and login
  - 2FA setup/complete/disable/method change
  - OAuth start/complete
  - OAuth account link/unlink/list
  - Refresh token rotation
  - Session listing and session revoke
- Applies feature flags to enable/disable endpoint groups at runtime.
- Uses JWT bearer auth for protected endpoints.
- Uses Swagger for API testing.

## Startup wiring

`Program.cs` currently registers:

- Communications providers (email/sms)
- Azure Table repository stores
- Identity services (`AddIBeamIdentityServices`)
- OTP auth service
- Password auth service
- OAuth auth service
- Memory cache and HttpClient (OAuth)

## Required configuration

### `IBeam:Identity:AzureTable`

Storage and table names for identity persistence (required when using Azure Table repository).

### `IBeam:Identity:Jwt`

- `Issuer`
- `Audience`
- `SigningKey`
- `AccessTokenMinutes`
- `PreTenantTokenMinutes`
- `RefreshTokenDays`
- `ClockSkewSeconds`

### `IBeam:Identity:Otp`

OTP generation/verification settings:

- `CodeLength`
- `ExpirationMinutes`
- `MaxAttempts`
- `HashSalt`
- `VerificationTokenSecret`
- `VerificationTokenMinutes`

### `IBeam:Identity:Features`

Runtime endpoint switches:

- `Otp`
- `PasswordAuth`
- `TwoFactor`
- `OAuth`
- `TenantSelection`
- `ClaimsEnrichment`

### `IBeam:Identity:OAuth`

OAuth provider definitions and endpoints for Google/Microsoft (or additional providers).

### Communications settings

- `IBeam:Communications:Email:*`
- `IBeam:Communications:Sms:*`

These are used by OTP and verification emails/sms.

## Running locally

```bash
dotnet restore
dotnet build
dotnet run --project IBeam.Identity.Api
```

Swagger UI:

- `/swagger`
