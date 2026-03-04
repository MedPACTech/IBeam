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
- `AddIBeamAuthEvents(configuration)` for auth lifecycle events (optional explicit call; already included by `AddIBeamIdentityServices`)
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

### `IBeam:Identity:Events`

- `StrictPublishFailures` (default `false`)
  - `false`: lifecycle hook/publisher failures are logged and auth continues.
  - `true`: lifecycle hook/publisher failures fail the auth request.

## Auth lifecycle events

Identity lifecycle events are emitted from OTP, password, OAuth, and token/session flows.

Provisioning events:

- `AuthUserCreatedEvent`
- `TenantCreatedEvent`
- `TenantUserLinkedEvent`

Additional lifecycle events:

- `AuthUserCreateRequestedEvent`
- `TenantCreateRequestedEvent`
- `TenantUserLinkRequestedEvent`
- `LoginAttemptedEvent`
- `LoginSucceededEvent`
- `LoginFailedEvent`
- `OtpChallengeCreatedEvent`
- `OtpVerifiedEvent`
- `OtpVerificationFailedEvent`
- `TokenIssuedEvent`
- `RefreshTokenRotatedEvent`
- `SessionRevokedEvent`

Common event fields:

- `EventId`
- `EventType`
- `OccurredUtc`
- `CorrelationId`
- `CausationId`
- `TraceId`
- `Metadata["idempotencyKey"]`

Current emission order for OTP new-user provisioning:

1. `AuthUserCreatedEvent`
2. `TenantCreatedEvent`
3. `TenantUserLinkedEvent`

Order is guaranteed within a single request execution path.
Retries are delegated to the configured publisher implementation.
Default publisher/hook are no-op implementations.

## Subscriber example

```csharp
public sealed class UserProfileHook : IAuthLifecycleHook
{
    private readonly IUserProfileRepository _profiles;

    public UserProfileHook(IUserProfileRepository profiles)
    {
        _profiles = profiles;
    }

    public Task OnAuthUserCreatedAsync(AuthUserCreatedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnTenantCreatedAsync(TenantCreatedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task OnTenantUserLinkedAsync(TenantUserLinkedEvent evt, CancellationToken ct = default)
    {
        if (!Guid.TryParse(evt.AuthUserId, out var userId))
            return;

        await _profiles.UpsertByTenantUserAsync(evt.TenantId, userId, ct);
    }
}
```

```csharp
services.AddIBeamIdentityServices(configuration);
services.AddScoped<IAuthLifecycleHook, UserProfileHook>();
```

## Build

```bash
dotnet restore
dotnet build
```
