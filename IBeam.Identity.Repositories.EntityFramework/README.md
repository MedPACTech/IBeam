# IBeam.Identity.Repositories.EntityFramework

Entity Framework identity repository provider for IBeam.

## Narrative Introduction

This package offers EF-based Identity store wiring and tenant membership persistence for teams that prefer relational storage. It centralizes provider selection and DbContext setup behind one registration method so hosts can swap persistence approaches without changing auth orchestration code.

## Features and Components

- DI extension:
  - `AddIBeamIdentityEntityFrameworkStores(IServiceCollection, IConfiguration, string configSectionPath = "IdentityEf")`
- `IBeamIdentityDbContext` registration
- ASP.NET Core Identity EF store wiring
- tenant membership store implementation

## Dependencies

- Internal packages:
  - `IBeam.Identity.Services`
  - `IBeam.Identity`
- External packages:
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore.Sqlite`
  - `Microsoft.EntityFrameworkCore.SqlServer`
  - `Npgsql.EntityFrameworkCore.PostgreSQL`
  - `System.IdentityModel.Tokens.Jwt`
  - `Microsoft.AspNetCore.App` framework reference

## Provider Status

- Supported now: `Sqlite`
- Not yet active in extension wiring: `SqlServer`, `Postgres`
