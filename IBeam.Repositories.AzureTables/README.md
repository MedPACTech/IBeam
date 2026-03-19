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
