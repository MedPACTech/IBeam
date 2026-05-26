# IBeam.Communications

`IBeam.Communications` is the provider-agnostic communications foundation for IBeam.

## Narrative Introduction

This package gives application code a stable abstraction for email and SMS delivery without coupling your domain services to a specific transport provider. Provider packages (Azure Communications, SMTP, SendGrid, etc.) plug into these contracts so your application logic can stay unchanged when delivery backends change.

## Features and Components

- Core service contracts:
  - `IEmailService`
  - `ISmsService`
  - `ITemplatedEmailService`
  - `IEmailTemplateRenderer`
- Message models:
  - `EmailMessage`, `EmailAddress`, `SmsMessage`, `RenderedEmailTemplate`
- Options and validation:
  - `EmailOptions`, `SmsOptions`, `EmailTemplateOptions`
  - built-in option validation and startup validation support
- Templating helpers:
  - `FileSystemEmailTemplateRenderer`
  - `TemplatedEmailService`
- DI entry point:
  - `AddIBeamCommunications(IConfiguration)`

## Dependencies

- External packages:
  - `Microsoft.Extensions.Configuration.Abstractions`
  - `Microsoft.Extensions.Options`
  - `Microsoft.Extensions.Options.ConfigurationExtensions`
- Internal packages:
  - none

## Quick Start

```csharp
builder.Services.AddIBeamCommunications(builder.Configuration);
```

Common configuration paths:
- `IBeam:Communications:Email`
- `IBeam:Communications:Sms`
- `IBeam:EmailTemplating`
