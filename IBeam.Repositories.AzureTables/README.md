# IBeam.Repositories.AzureTables

Azure Table Storage repository provider for `IBeam.Repositories`.

## Narrative Introduction

This package adds Azure Tables as a concrete data-store implementation for IBeam repositories. It supports configurable storage models, key formatting, entity mapping overrides, and optional locator-based ID lookup patterns for tenant-aware workloads.

## Features and Components

- registration methods:
  - `ConfigureIBeamAzureTables(IConfiguration)`
  - `ConfigureIBeamAzureTables(Action<AzureTablesOptions>)`
  - `AddIBeamAzureTablesRepositories()`
- repository/store implementations:
  - `AzureTablesRepositoryAsync<T>`
  - `AzureTablesRepositoryStore<T>`
- mapping and key services:
  - `IAzureEntityKeyResolver<T>`
  - `IAzureEntityKeyFormatter`
  - `IEntityLocator`
  - `AddAzureEntityMapping<T>(...)`
- partition strategy helpers and in-memory locator option

## Dependencies

- Internal packages:
  - `IBeam.Repositories`
  - `IBeam.Utilities`
- External packages:
  - `Azure.Data.Tables`
  - `Microsoft.Extensions.Options`

## Configuration Section

- `IBeam:Repositories:AzureTables`

## Table Name Prefix

Repository table names are built from:

`{TableNamePrefix}{EntityOrMappedTableName}`

`TableNamePrefix` is configured at `IBeam:Repositories:AzureTables:TableNamePrefix`. If it is unset, the generic repository provider uses an empty prefix. IBeam does not derive a prefix from environment name, application name, or connection string; values such as `WellderlyTest` only appear when explicitly configured.

This setting is separate from the identity Azure Table provider setting, `IBeam:Identity:AzureTable:TablePrefix`.

`IBeam:Repositories:AzureTables:CreateTablesIfNotExists` controls whether repository tables are created on first use. The default is `true`.

## Connection String Cascade

AzureTables provider resolves connection string with fallback precedence:

1. `IBeam:Repositories:AzureTables:ConnectionString`
2. `IBeam:AzureTables`
3. `IBeam:Repositories:ConnectionString`
4. `IBeam:ConnectionString`
5. `ConnectionStrings:AzureTables`
6. `ConnectionStrings:AzureStorage`
7. `ConnectionStrings:IBeam`
8. `ConnectionStrings:DefaultConnection`
