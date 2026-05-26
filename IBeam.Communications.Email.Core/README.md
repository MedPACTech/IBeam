# IBeam.Communications.Email.Templating

`IBeam.Communications.Email.Templating` provides file-based template rendering and templated-email orchestration for the IBeam communications stack.

## Narrative Introduction

Use this package when your email workflow starts from named templates rather than hand-built HTML/text bodies. It resolves template files from a configured base path, renders bodies, and then forwards the result through the registered `IEmailService` provider.

## Features and Components

- `FileEmailTemplateRenderer` for reading template files from disk
- `TemplatedEmailService` to orchestrate render + send
- `AddIBeamEmailTemplatingFromFiles(IConfiguration)` for DI registration
- startup validation for template base path

## Dependencies

- Internal packages:
  - `IBeam.Communications`
- External packages:
  - `Microsoft.Extensions.Options.ConfigurationExtensions`

## Quick Start

```csharp
builder.Services.AddIBeamEmailTemplatingFromFiles(builder.Configuration);
```

Required configuration section:
- `IBeam:EmailTemplating`
