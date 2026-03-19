# IBeam.Api

`IBeam.Api` is a lightweight ASP.NET Core helper library for composing consistent API behavior.

## Narrative Introduction

This package is designed for teams that want predictable API response envelopes and centralized exception handling without forcing a full framework. It keeps `Program.cs` focused by providing extension-based wiring for configuration, DI registration groups, and middleware setup.

## Features and Components

- `ApiControllerBase` with consistent response helpers
- global exception handling middleware (`ApiExceptionMiddleware`)
- background error sink bridge (`GlobalErrorHandler`)
- API response models (`ApiResponse<T>`, `ApiPagedResponse<T>`, `ApiError`)
- composition helpers:
  - `AddIBeamApi(...)`
  - `UseIBeamApi()`

## Dependencies

- Internal packages:
  - `IBeam.Utilities`
- External runtime dependency:
  - `Microsoft.AspNetCore.App` framework reference

## Quick Start

```csharp
builder.Services.AddIBeamApi(builder.Configuration, api =>
{
    // optional grouped registrations
});

app.UseIBeamApi();
```
