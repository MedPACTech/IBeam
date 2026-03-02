# IBeam.Identity.Api

`IBeam.Identity.Api` is a reusable API module for Identity authentication and account endpoints.

## What this project does

- Exposes REST endpoints for:
  - OTP start/complete
  - Email/password registration and login
  - 2FA setup/complete/disable/method change
  - OAuth start/complete
  - OAuth account link/unlink/list
  - Refresh token rotation
  - Session listing and session revoke
- Applies feature flags to enable/disable endpoint groups at runtime.
- Uses JWT bearer auth for protected endpoints.
- Is intended to be consumed by another ASP.NET Core API host.

## Consumer wiring

In the consuming API:

```csharp
using IBeam.Identity.Api.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIBeamIdentityApi(builder.Configuration);
builder.Services.AddIBeamIdentityApiControllers();
```

Then in the pipeline:

```csharp
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

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

## Building

```bash
dotnet restore
dotnet build
```
