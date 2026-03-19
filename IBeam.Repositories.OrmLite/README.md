# IBeam.Repositories.OrmLite

ServiceStack OrmLite repository provider for `IBeam.Repositories`.

## Narrative Introduction

This package provides a relational repository implementation for teams using ServiceStack OrmLite. It allows service-layer code to keep using IBeam repository abstractions while selecting SQL provider behavior through OrmLite drivers.

## Features and Components

- `OrmLiteRepositoryStore<T>`
- `OrmLiteRepositoryAsync<T>`
- `IOrmLiteRepositoryAsync<T>`
- DI extension:
  - `AddIBeamOrmLiteRepositories()`

## Dependencies

- Internal packages:
  - `IBeam.Repositories`
  - `IBeam.Utilities`
- External packages:
  - `Microsoft.Extensions.Options`
  - `ServiceStack.OrmLite`
  - `ServiceStack.OrmLite.SqlServer`
  - `ServiceStack.OrmLite.Sqlite`
  - `ServiceStack.OrmLite.PostgreSQL.Core`
