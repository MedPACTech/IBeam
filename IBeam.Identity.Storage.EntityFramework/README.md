# IBeam.Identity.Storage.EntityFramework

This folder currently does not contain active source code.

## Current status

- Only `bin/` and `obj/` artifacts are present.
- The active EF implementation lives in:
  - `IBeam.Identity.Repositories.EntityFramework`

## Use this instead

For Entity Framework identity persistence, use:

- Project: `IBeam.Identity.Repositories.EntityFramework`
- Registration: `AddIBeamIdentityEntityFrameworkStores(services, configuration, "IdentityEf")`
- Config section: `IdentityEf`

### Key settings

- `Provider` (`Sqlite`, `SqlServer`, `Postgres`)
- `ConnectionString`
- `MigrationsAssembly` (optional)

Note: current extension implementation is usable with `Sqlite`; other provider enum values exist but may require additional provider wiring.

If this folder is intended to be revived, add a `.csproj` and source files, then update this README.
