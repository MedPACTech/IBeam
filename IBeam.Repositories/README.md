# IBeam.Repositories

Core repository abstractions and base implementations for IBeam.

## Narrative Introduction

This package defines the repository contract layer that service packages build on. It standardizes CRUD, archival, deletion semantics, and tenant-aware patterns so backing stores can vary (Azure Tables, OrmLite, etc.) without changing application services.

## Features and Components

- entity contracts:
  - `IEntity`
  - `ITenantEntity`
  - `IArchivableEntity`
  - `IDeletableEntity`
- repository contracts:
  - `IRepositoryStore<T>`
  - `IBaseRepository<T>`
  - `IBaseRepositoryAsync<T>`
  - `IArchivableRepositoryAsync<T>`
- base implementations:
  - `BaseRepository<T>`
  - `BaseRepositoryAsync<T>`
- cross-cutting types:
  - `TenantContext`
  - `RepositoryOptions`
  - repository exception hierarchy
  - batch action model

## Dependencies

- External packages:
  - `Microsoft.Extensions.Caching.Abstractions`
- Internal packages:
  - none
