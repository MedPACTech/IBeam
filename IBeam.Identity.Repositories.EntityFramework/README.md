# IBeam.Identity.Repositories.EntityFramework

`IBeam.Identity.Repositories.EntityFramework` is the Entity Framework-backed repository package for Identity.

## What this project does

- Provides EF Core Identity store wiring and EF-based tenant membership store.
- Registers `IBeamIdentityDbContext` and ASP.NET Identity EF stores.
- Exposes extension method:
  - `AddIBeamIdentityEntityFrameworkStores(IServiceCollection, IConfiguration, string configSectionPath = "IdentityEf")`

## Current status

- Implemented and usable with `Sqlite` provider.
- `SqlServer` and `Postgres` enum values exist, but extension currently throws if selected.

## Required configuration

Section (default): `IdentityEf`

- `Provider` (`Sqlite`, `SqlServer`, `Postgres`)
- `ConnectionString`
- `MigrationsAssembly` (optional)

Example:

```json
{
  "IdentityEf": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=ibeam.identity.db",
    "MigrationsAssembly": "IBeam.Identity.Repositories.EntityFramework"
  }
}
```

## Usage

```csharp
builder.Services.AddIBeamIdentityEntityFrameworkStores(builder.Configuration);
```

## Build

```bash
dotnet restore
dotnet build
```
