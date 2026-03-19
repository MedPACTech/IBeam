# IBeam

IBeam is a modular .NET framework for building identity, communications, repository, and service-layer capabilities as composable packages.

## Open Source Readiness Goals

This repository is being prepared for open source distribution. Each package README now focuses on:
- what the package is responsible for
- the main features and components it exposes
- dependency expectations (IBeam packages + external packages)
- minimum startup wiring and configuration paths

## Package Map

### API
- `IBeam.Api`: reusable API composition helpers (response envelopes, exception middleware, DI/config builder)
- `IBeam.Identity.Api`: identity endpoint module that composes identity services + providers

### Communications
- `IBeam.Communications`: provider-agnostic email/SMS contracts, options, validation, templating orchestration
- `IBeam.Communications.Email.Templating`: file-based email template renderer and templated send orchestration
- `IBeam.Communications.Email.AzureCommunications`: Azure Communication Services email provider
- `IBeam.Communications.Email.SendGrid`: SendGrid email provider
- `IBeam.Communications.Email.Smtp`: SMTP email provider
- `IBeam.Communications.Email.PickupDirectory`: local filesystem email pickup provider
- `IBeam.Communications.Sms.AzureCommunications`: Azure Communication Services SMS provider
- `IBeam.Communications.Sms.Twilio`: reserved package for Twilio SMS provider (currently scaffold state)

### Identity
- `IBeam.Identity`: identity contracts, models, options, events, and schema abstractions
- `IBeam.Identity.Services`: identity orchestration (OTP, password, OAuth, tokens, tenant selection)
- `IBeam.Identity.Repositories.AzureTable`: Azure Table-backed identity stores and schema bootstrap
- `IBeam.Identity.Repositories.EntityFramework`: EF-backed identity store wiring (Sqlite currently active)

### Data and Services
- `IBeam.Repositories`: repository abstractions and base implementations
- `IBeam.Repositories.AzureTables`: Azure Table repository implementation
- `IBeam.Repositories.OrmLite`: ServiceStack OrmLite repository implementation
- `IBeam.Services`: service abstractions + base service implementations and operation policy resolver
- `IBeam.Services.AutoMapper`: `IModelMapper<TEntity,TModel>` bridge powered by AutoMapper

### Utilities
- `IBeam.Utilities`: shared utility primitives (auditing, exception middleware, cache and token helpers)

## Build

```bash
dotnet restore
dotnet build IBeam.sln
```
