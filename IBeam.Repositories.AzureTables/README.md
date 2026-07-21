# IBeam.Repositories.AzureTables

`IBeam.Repositories.AzureTables` provides a generic Azure Table Storage repository provider for `IBeam.Repositories`.

```powershell
dotnet add package IBeam.Repositories.AzureTables
```

## When To Use This

- You want IBeam repository abstractions backed by Azure Table Storage.
- You need tenant-aware partition strategies.
- You want configurable table names, key formatting, and storage models.
- You are not using a domain-specific repository package such as `IBeam.Identity.Repositories.AzureTable`.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Repository | `AzureTablesRepositoryAsync<T>`, `IAzureTablesRepositoryAsync<T>` | Implements IBeam async repository contracts. |
| Store | `AzureTablesRepositoryStore<T>`, `IAzureTablesRepositoryStore<T>` | Performs Azure Table persistence. |
| Key resolution | `IAzureEntityKeyResolver<T>`, `IAzureEntityKeyFormatter`, `IEntityKeyBinder<T>` | Converts entities and IDs into table keys. |
| Partition strategies | `AzureTablePartitionKeyStrategies` helpers | Supports global, tenant, tenant-hash-bucket, or delegate strategies. |
| Entity mapping | `AddAzureEntityMapping<T>(...)`, `AzureEntityMappingOptions<T>` | Overrides table name or model behavior per entity type. |
| Locator | `IEntityLocator`, `AddInMemoryEntityLocator()` | Optional ID-to-key lookup support for non-obvious partitions. |
| Options | `AzureTablesOptions` | Connection string, prefix, storage model, key format, table creation. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is a repository provider. It should persist one entity type per repository. It should not contain service rules, permission checks, or audit orchestration.

## Quick Start

```csharp
using IBeam.Repositories.AzureTables;

builder.Services.ConfigureIBeamAzureTables(builder.Configuration);
builder.Services.AddIBeamAzureTablesRepositories();
```

Configuration:

```json
{
  "IBeam": {
    "Repositories": {
      "AzureTables": {
        "ConnectionString": "<storage-connection-string>",
        "TableNamePrefix": "IBeam",
        "CreateTablesIfNotExists": true,
        "StorageModel": "Envelope",
        "GuidKeyFormat": "N",
        "EnableLegacyGuidKeyFallbackReads": false
      }
    }
  }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `ConnectionString` | required | Azure Storage account connection string. |
| `TableNamePrefix` | empty | Prefix applied to generated/mapped table names. |
| `CreateTablesIfNotExists` | `true` | Creates tables on first use. Disable if schema is provisioned externally. |
| `StorageModel` | `Envelope` | `Envelope` stores JSON in `Data`; `EntityColumns` stores public properties as table columns. |
| `GuidKeyFormat` | `N` | Format used for Guid row keys. |
| `EnableLegacyGuidKeyFallbackReads` | `false` | Enables fallback reads for legacy Guid key formats. |

Connection string fallback precedence:

1. `IBeam:Repositories:AzureTables:ConnectionString`
2. `IBeam:AzureTables`
3. `IBeam:Repositories:ConnectionString`
4. `IBeam:ConnectionString`
5. `ConnectionStrings:AzureTables`
6. `ConnectionStrings:AzureTable`
7. `ConnectionStrings:AzureStorage`
8. `ConnectionStrings:IBeam`
9. `ConnectionStrings:DefaultConnection`

## Table Naming

Repository table names are built from:

```text
{TableNamePrefix}{EntityOrMappedTableName}
```

With `TableNamePrefix = "IBeam"` and entity type `Product`, the default table is `IBeamProduct`.

Override a table name:

```csharp
builder.Services.AddAzureEntityMapping<Product>(options =>
{
    options.TableName = "Products";
});
```

With the same prefix, the physical table becomes `IBeamProducts`.

## Azure Table Schema

Generic repository tables are entity-specific. Each entity type maps to its own table unless `AddAzureEntityMapping<T>` changes the table name.

### Envelope Storage Model

| Field | Purpose |
|---|---|
| `PartitionKey` | Computed by the configured partition strategy. |
| `RowKey` | Entity `Id` formatted by `GuidKeyFormat`. |
| `Timestamp` | Azure Table server timestamp. |
| `ETag` | Azure Table concurrency token. |
| `Data` | JSON copy of the entity. |
| `Type` | Optional entity type marker. |

### EntityColumns Storage Model

| Field | Purpose |
|---|---|
| `PartitionKey` | Computed by the configured partition strategy. |
| `RowKey` | Entity `Id` formatted by `GuidKeyFormat`. |
| `Timestamp` | Azure Table server timestamp. |
| `ETag` | Azure Table concurrency token. |
| entity public properties | Stored as Azure Table columns where supported. |

## Partition And Row Keys

| Strategy | PartitionKey | RowKey | When To Use |
|---|---|---|---|
| Default | Tenant-aware when possible, otherwise provider default | Entity `Id` | General use. |
| Global | fixed value such as `global` | Entity `Id` | Small lookup tables shared across tenants. |
| Tenant | tenant ID | Entity `Id` | Tenant-scoped data with tenant queries. |
| Tenant hash bucket | tenant ID plus hash bucket | Entity `Id` | Large tenant partitions that need write distribution. |
| Delegate | host-defined | host-defined/ID-based | Specialized domain models. |

Example:

```csharp
builder.Services.UseTenantPartitionKey<Product>();
builder.Services.UseTenantHashBucketPartitionKey<ActivityLog>(bucketCount: 32);
```

## Data Storage Notes

This package creates generic repository tables only when `CreateTablesIfNotExists` is `true`. It does not create Identity, AccessControl, Logging, or Licensing tables owned by other packages.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)

Agents should document entity-specific table mappings in the consuming project when using this package.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup fails with missing connection string | No supported connection setting found | Add one of the supported connection settings. |
| Table name is unexpected | Prefix or mapping changed it | Check `TableNamePrefix` and `AddAzureEntityMapping<T>`. |
| Entity lookup by ID fails | Partition cannot be inferred | Use tenant-aware calls, a locator, or an explicit partition strategy. |
| Large partition hot spots | Too much traffic in one partition | Consider tenant hash bucket partitioning. |

## Version Notes

- Targets `net10.0`.
- Uses `Azure.Data.Tables`.
- Package version is assigned by the repository release workflow.
