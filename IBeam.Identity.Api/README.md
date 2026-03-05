# IBeam.Identity.Api

Reusable Identity API module for OTP, password, OAuth, token, and session endpoints.

## Startup Registration

```csharp
using IBeam.Identity.Api.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIBeamIdentityApi(builder.Configuration);
builder.Services.AddIBeamIdentityApiControllers();
```

Pipeline:

```csharp
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

Notes:
- `AddIBeamIdentityApi(...)` already calls `AddIBeamIdentityServices(...)`.
- Do not call `AddIBeamIdentityServices(...)` again unless you intentionally override registrations.
- `AddIBeamIdentityApiControllers()` only adds MVC controller parts; it does not wire services/auth.

## Lifecycle Hooks (Consumer App)

Register hooks from your own app/services assembly:

```csharp
builder.Services.AddScoped<IAuthLifecycleHook, UserProfileHook>();
```

Make sure the containing project is referenced by the API host.

## Required Configuration

### Identity core
- `IBeam:Identity:Jwt`
- `IBeam:Identity:Otp`
- `IBeam:Identity:Features`
- `IBeam:Identity:OAuth` (if OAuth enabled)
- `IBeam:Identity:Events` (optional strict event failure mode)

### Identity persistence
- `IBeam:Identity:AzureTable` (or your selected identity repository provider)

### Communications
- `IBeam:Communications:Email:Providers:AzureCommunications:*`
- `IBeam:Communications:Sms:Providers:AzureCommunications:*`

## Endpoints Included

- OTP: start/complete
- Password: registration/login
- 2FA: setup/start/complete/disable/method
- OAuth: start/complete/link/unlink/list
- Tokens: refresh
- Sessions: list/revoke
