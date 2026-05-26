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

## Connection String Cascade

EF identity store registration resolves connection string with fallback precedence:

1. `{configSectionPath}:ConnectionString` (default section path is `IdentityEf`)
2. `IBeam:Identity:EntityFramework:ConnectionString`
3. `IBeam:Repositories:EntityFramework:ConnectionString`
4. `IBeam:Repositories:ConnectionString`
5. `IBeam:ConnectionString`
6. `ConnectionStrings:IdentityEf`
7. `ConnectionStrings:IdentityEntityFramework`
8. `ConnectionStrings:IBeam`
9. `ConnectionStrings:DefaultConnection`

This aligns EF identity provider behavior with the broader IBeam repository fallback pattern.
