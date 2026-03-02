# IBeam.Identity.Abstractions

`IBeam.Identity.Abstractions` contains contracts and shared models for the Identity platform.

## What this project does

- Defines service interfaces for auth flows:
  - Password auth (`IIdentityAuthService`)
  - OTP auth (`IIdentityOtpAuthService`)
  - OAuth auth (`IIdentityOAuthAuthService`)
  - Token/session management (`ITokenService`)
- Defines storage contracts:
  - User store (`IIdentityUserStore`)
  - OTP challenge store (`IOtpChallengeStore`)
  - Tenant membership/provisioning stores
  - External login store (`IExternalLoginStore`)
  - Auth session store (`IAuthSessionStore`)
- Defines shared request/response models and option classes.

This project has no runtime implementation. It is referenced by API, Services, and Repositories.

## Key configuration models

These options classes are consumed by implementation projects:

- `JwtOptions` (`IBeam:Identity:Jwt`)
- `OtpOptions` (`IBeam:Identity:Otp`)
- `OAuthOptions` (`IBeam:Identity:OAuth`)
- `FeatureOptions` (`IBeam:Identity:Features`)
- `IdentityOptions` (`IBeam:Identity`) wrapper

## Build

```bash
dotnet restore
dotnet build
```
