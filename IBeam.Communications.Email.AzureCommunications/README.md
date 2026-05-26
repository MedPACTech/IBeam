# IBeam.Communications.Email.AzureCommunications

Azure Communication Services email provider for `IBeam.Communications`.

## Narrative Introduction

This package is the production-ready ACS email transport for IBeam. It keeps your application on `IEmailService` while handling ACS-specific connection resolution, option validation, and sender defaults behind the provider boundary.

## Features and Components

- `AzureCommunicationsEmailService : IEmailService`
- `AzureCommunicationsEmailOptions`
- DI registration via `AddIBeamAzureCommunicationsEmail(IConfiguration)`
- deterministic connection string resolution with fallback order
- startup validation for required connection string

## Dependencies

- Internal packages:
  - `IBeam.Communications`
- External packages:
  - `Azure.Communication.Email`
  - `Microsoft.Extensions.Configuration.Abstractions`
  - `Microsoft.Extensions.Options`
  - `Microsoft.Extensions.Options.ConfigurationExtensions`

## Quick Start

```csharp
builder.Services.AddIBeamAzureCommunicationsEmail(builder.Configuration);
```

Primary configuration section:
- `IBeam:Communications:Email:Providers:AzureCommunications`
