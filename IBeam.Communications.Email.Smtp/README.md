# IBeam.Communications.Email.Smtp

SMTP email provider for `IBeam.Communications`.

## Narrative Introduction

This package gives IBeam a standards-based SMTP transport. It is useful when your environment already has an SMTP relay or when you need a provider-neutral path that still fits the `IEmailService` abstraction.

## Features and Components

- `SmtpEmailService : IEmailService`
- `SmtpEmailOptions`
- DI registration via `AddIBeamSmtpEmail(IConfiguration)`
- startup validation for host, port, and default sender

## Dependencies

- Internal packages:
  - `IBeam.Communications`
- External packages:
  - `Microsoft.Extensions.Configuration.Abstractions`
  - `Microsoft.Extensions.Options`
  - `Microsoft.Extensions.Options.ConfigurationExtensions`

## Quick Start

```csharp
builder.Services.AddIBeamSmtpEmail(builder.Configuration);
```

Configuration section:
- `IBeam:Communications:Email:Smtp`
