# IBeam.Communications.Sms.AzureCommunications

Azure Communication Services SMS provider for `IBeam.Communications`.

## Narrative Introduction

This package implements `ISmsService` using Azure Communication Services SMS. It keeps SMS delivery behind IBeam abstractions while handling ACS option binding, validation, and connection fallback rules.

## Features and Components

- `AzureCommunicationsSmsService : ISmsService`
- `AzureCommunicationsSmsOptions`
- DI registration via `AddIBeamCommunicationsSmsAzure(IConfiguration)`
- startup validation for required connection string

## Dependencies

- Internal packages:
  - `IBeam.Communications`
- External packages:
  - `Azure.Communication.Sms`

## Quick Start

```csharp
builder.Services.AddIBeamCommunicationsSmsAzure(builder.Configuration);
```

Primary configuration section:
- `IBeam:Communications:Sms:Providers:AzureCommunications`
