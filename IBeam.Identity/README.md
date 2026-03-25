# IBeam.Identity

`IBeam.Identity` is the contract package for the IBeam identity domain.

## Narrative Introduction

This package provides the shared language for identity workflows across API, services, and repository implementations. It contains interfaces, request/response models, options, and event contracts so all higher-level packages can evolve independently behind stable abstractions.

## Features and Components

- auth service contracts:
  - `IIdentityAuthService`
  - `IIdentityOtpAuthService`
  - `IIdentityOAuthAuthService`
  - `ITokenService`
- store contracts:
  - `IIdentityUserStore`, `IOtpChallengeStore`, `IExternalLoginStore`
  - `ITenantMembershipStore`, `ITenantProvisioningService`, `IAuthSessionStore`
  - `ITenantRoleStore` for tenant-scoped role CRUD and assignment
  - `IPermissionAccessStore` for tenant permission->role mappings (dynamic access map)
- service contracts:
  - `ITenantRoleService` for role orchestration in app/service layers
  - `IRoleAccessAuthorizer` for enforcing role access attributes in non-API services
  - `IPermissionAccessAuthorizer` for dynamic permission-map authorization in services
- identity models and transport records
- options models (`JwtOptions`, `OtpOptions`, `OAuthOptions`, `FeatureOptions`, etc.)
- lifecycle event contracts and default no-op implementations
- role access attributes (service-safe, no MVC dependency):
  - `[RoleAccess("owner", "billing")]`
  - `[RoleAccessId("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9")]`
  - `[AllowAllRoleAccess]` to allow all authenticated users for a class/method
- dynamic permission attributes (external role mapping):
  - `[PermissionAccess("SavePatient")]`
  - `[PermissionAccessId("6c76f166-b130-4c80-bf7e-99d38ea1a75f")]`

## Dependencies

- External packages: none
- Internal packages: none

## Configuration Models Exposed

- `IBeam:Identity:Jwt`
- `IBeam:Identity:Otp`
- `IBeam:Identity:OAuth`
- `IBeam:Identity:Features`
- `IBeam:Identity:Events`
- `IBeam:Identity:EmailTemplates`
- `IBeam:Identity:PermissionAccess`

## Service Role Access Example

```csharp
[RoleAccess("SavePatient")]
public sealed class PatientService
{
    private readonly IRoleAccessAuthorizer _roleAccess;

    public PatientService(IRoleAccessAuthorizer roleAccess)
    {
        _roleAccess = roleAccess;
    }

    public Task SavePatientAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        _roleAccess.EnsureAuthorizedForCurrentMethod(user, this);
        // service logic...
        return Task.CompletedTask;
    }

    [AllowAllRoleAccess]
    public Task GetSummaryAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        _roleAccess.EnsureAuthorizedForCurrentMethod(user, this);

        // Internal branch check: run extra side effects only for non-admins.
        if (!user.HasAnyRole("admin", "administrator"))
        {
            // send notification, enqueue event, etc.
        }

        return Task.CompletedTask;
    }
}
```
