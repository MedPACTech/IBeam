# IBeam.Services.AutoMapper

AutoMapper integration package for `IBeam.Services` model mapping.

## Narrative Introduction

This package bridges `IModelMapper<TEntity,TModel>` to AutoMapper so IBeam services can depend on framework abstractions while still using AutoMapper profiles for actual mapping logic.

## Features and Components

- `AutoMapperModelMapper<TEntity,TModel>` implementation
- DI extension:
  - `AddIBeamAutoMapper(params Assembly[] profileAssemblies)`
- scoped registration of `IModelMapper<,>` mapped to AutoMapper-backed implementation

## Dependencies

- Internal packages:
  - `IBeam.Services`
- External packages:
  - `AutoMapper`
  - `Microsoft.Extensions.Options`

## Quick Start

```csharp
builder.Services.AddIBeamAutoMapper(typeof(MyMappingProfile).Assembly);
```
