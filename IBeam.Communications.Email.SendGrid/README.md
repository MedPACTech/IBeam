# IBeam.Communications.Email.SendGrid

SendGrid email provider for `IBeam.Communications`.

## Narrative Introduction

This package integrates SendGrid as a drop-in `IEmailService` implementation. It is intended for teams that want provider-level reliability while keeping application services anchored to IBeam abstractions instead of direct SendGrid SDK usage.

## Features and Components

- `SendGridEmailService : IEmailService`
- `SendGridEmailOptions`
- address mapping helpers (`SendGridAddressMapper`)
- DI registration via `AddIBeamSendGridEmail(IConfiguration)`
- startup validation for API key and default sender

## Dependencies

- Internal packages:
  - `IBeam.Communications`
- External packages:
  - `SendGrid`
  - `SendGrid.Extensions.DependencyInjection`
  - `Microsoft.Extensions.Configuration.Abstractions`
  - `Microsoft.Extensions.Options`
  - `Microsoft.Extensions.Options.ConfigurationExtensions`

## Quick Start

```csharp
builder.Services.AddIBeamSendGridEmail(builder.Configuration);
```

Configuration section:
- `IBeam:Communications:Email:SendGrid`
