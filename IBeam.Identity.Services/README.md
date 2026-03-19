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
- DI extension methods:
  - `AddIBeamIdentityServices(IConfiguration)`
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
