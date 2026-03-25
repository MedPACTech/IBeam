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
- service contracts:
  - `ITenantRoleService` for role orchestration in app/service layers
- identity models and transport records
- options models (`JwtOptions`, `OtpOptions`, `OAuthOptions`, `FeatureOptions`, etc.)
- lifecycle event contracts and default no-op implementations

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
