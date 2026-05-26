# IBeam.Communications.Email.PickupDirectory

Filesystem pickup-directory email provider for `IBeam.Communications`.

## Narrative Introduction

This package is a non-production transport for local development and testing. Instead of delivering emails over a network provider, it writes `.eml` messages to disk so developers and test suites can inspect exact output safely.

## Features and Components

- `PickupDirectoryEmailService : IEmailService`
- `PickupDirectoryEmailOptions`
- DI registration via `AddIBeamPickupDirectoryEmail(IConfiguration)`
- startup validation for directory path and default sender

## Dependencies

- Internal packages:
  - `IBeam.Communications`
- External packages:
  - `Microsoft.Extensions.Configuration.Abstractions`
  - `Microsoft.Extensions.Options`
  - `Microsoft.Extensions.Options.ConfigurationExtensions`

## Quick Start

```csharp
builder.Services.AddIBeamPickupDirectoryEmail(builder.Configuration);
```

Configuration section:
- `IBeam:Communications:Email:PickupDirectory`

Recommended use:
- local development
- integration tests
- QA verification of template output
