# IBeam
[![CI](https://github.com/MedPACTech/IBeam/actions/workflows/ci.yml/badge.svg)](https://github.com/MedPACTech/IBeam/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/IBeam.Services)](https://www.nuget.org/packages/IBeam.Services)
[![NuGet Downloads](https://img.shields.io/nuget/dt/IBeam.Services)](https://www.nuget.org/packages/IBeam.Services)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/MedPACTech/IBeam)](https://github.com/MedPACTech/IBeam/releases)

IBeam is a modular .NET framework for teams that want to move from idea to working API quickly, without giving up architecture quality.

The framework provides a strong base API and reusable application patterns so developers can stand up production-ready services with minimal implementation code. From there, teams can compose only the modules they need, extend where they want full control, and avoid rewriting the same plumbing in every new project.

## Why IBeam Exists
Most teams repeatedly solve the same cross-cutting concerns before they can deliver product value:
- service orchestration,
- data access patterns,
- identity flows,
- communication providers,
- logging and auditing,
- storage integration,
- API consistency.

IBeam packages these concerns into composable building blocks.

Developers can use IBeam on its own, alongside existing libraries, or with AI-assisted workflows to rapidly scaffold and evolve APIs. The goal is not to lock teams into one way of building. The goal is to provide a stable foundation and extension points so teams can customize intelligently.

## Design Goals
- Fast startup: get a base API running with very little custom code.
- Modular by default: add only the packages you need.
- Extension-first: override behavior with your own services/providers.
- Provider flexibility: choose storage, messaging, and identity integrations per project.
- Production mindset: testing, configuration, and operational patterns are first-class.

## Open-Core and Open Source Commitment
IBeam uses an open-core model:
- Core framework: Apache-2.0 open source
- Enterprise add-ons: commercial terms for premium/enterprise modules

This allows individuals and small teams to build freely while supporting long-term sustainability for enterprise-scale usage.

See:
- `LICENSE`
- `LICENSE-COMMERCIAL.md`
- `docs/licensing.md`

## Forking and Community Contributions
Forking is welcome for the Apache-2.0 core.

Contributions are encouraged across:
- bug fixes,
- new extension packages,
- documentation quality,
- integration examples,
- test coverage.

See `CONTRIBUTING.md` for workflow and standards.

## Community and Security
- Code of Conduct: `CODE_OF_CONDUCT.md`
- Security Policy: `SECURITY.md`

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
- `IBeam.Services.Logging`: optional service auditing/logging sinks and actor providers

### Storage
- `IBeam.Storage.Abstractions`: common blob storage contracts
- `IBeam.Storage.AzureBlobs`: Azure Blob Storage implementation
- `IBeam.Storage.FileSystem`: local and mounted filesystem blob implementation
- `IBeam.Storage.S3`: S3-compatible blob storage implementation

### Utilities
- `IBeam.Utilities`: shared utility primitives (auditing, exception middleware, cache and token helpers)

## Build

```bash
dotnet restore
dotnet build IBeam.sln
dotnet test IBeam.sln
```

## Roadmap Note
A public landing page is planned. See `docs/landing-page-plan.md`.
