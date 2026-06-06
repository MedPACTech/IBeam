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

## Cross-Pattern Auth Orchestration

`IBeam.Identity.Services` lets one user move between auth patterns without creating duplicate users. The service layer always works against `UserId` after the repository resolves an auth identifier.

Supported flows:

- OTP with SMS: `StartOtpAsync(phone)` then `CompleteOtpAsync(...)`.
- OTP with email: `StartOtpAsync(email)` then `CompleteOtpAsync(...)`.
- Email/password: `StartEmailPasswordRegistrationAsync(...)`, `CompleteEmailPasswordRegistrationAsync(...)`, then `PasswordLoginAsync(...)`.
- Add email/password to an existing SMS user: `StartEmailPasswordLinkAsync(...)`, then `CompleteEmailPasswordLinkAsync(...)`.
- Add SMS to an existing email user: `StartPhoneLinkAsync(...)`, then `CompletePhoneLinkAsync(...)`.
- 2FA: `StartTwoFactorSetupAsync(...)`, `CompleteTwoFactorSetupAsync(...)`, then `CompleteTwoFactorLoginAsync(...)`.

The repository provider is responsible for fast identifier resolution. For Azure Table, this is done by an `AuthIdentifiers` table keyed by identifier type and normalized value.

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

## Code Samples

### SMS OTP first, email/password later

```csharp
var otp = await otpAuth.StartOtpAsync("16145551212", ct: ct);
var signedIn = await otpAuth.CompleteOtpAsync(
    otp.ChallengeId,
    codeFromSms,
    "16145551212",
    displayName: "Adam",
    ct);

var userIdClaim = signedIn.Token!.Claims.First(c => c.Type == "uid").Value;
Guid userId = Guid.Parse(userIdClaim);

await passwordAuth.StartEmailPasswordLinkAsync(
    userId,
    "adam@test.com",
    resetUrlBase: "https://app.example.com/finish-email-link",
    ct: ct);

await passwordAuth.CompleteEmailPasswordLinkAsync(
    userId,
    "adam@test.com",
    challengeId,
    verificationToken,
    "new secure password",
    ct);
```

### Email user adds SMS

```csharp
var challenge = await passwordAuth.StartPhoneLinkAsync(userId, "16145551212", ct);

await passwordAuth.CompletePhoneLinkAsync(
    userId,
    "16145551212",
    challenge.ChallengeId,
    codeFromSms,
    ct);
```
