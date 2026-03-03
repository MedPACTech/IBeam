# IBeam.Repositories.AzureTables

Azure Table Storage provider for `IBeam.Repositories`.

## Configuration Structure

Yes, it follows the same `IBeam:*` pattern used across the solution.

Canonical section:

`IBeam:Repositories:AzureTables`

Options class:

- `AzureTablesOptions.SectionName = "IBeam:Repositories:AzureTables"`

### appsettings/application json example

```json
{
  "IBeam": {
    "Repositories": {
      "AzureTables": {
        "ConnectionString": "UseDevelopmentStorage=true",
        "TableNamePrefix": "IBeam",
        "CreateTablesIfNotExists": true,
        "StorageModel": "Envelope"
      }
    }
  }
}
```

`StorageModel` values:
- `Envelope`
- `EntityColumns`

## DI Registration

Bind from configuration:

```csharp
services.ConfigureIBeamAzureTables(configuration);
services.AddIBeamAzureTablesRepositories();
```

Or configure in code:

```csharp
services.ConfigureIBeamAzureTables(o =>
{
    o.ConnectionString = "UseDevelopmentStorage=true";
    o.TableNamePrefix = "IBeam";
    o.CreateTablesIfNotExists = true;
    o.StorageModel = AzureTableStorageModel.Envelope;
});

services.AddIBeamAzureTablesRepositories();
```

## What It Registers

- `IRepositoryStore<T>` -> `AzureTablesRepositoryStore<T>`
- `IAzureTablesRepositoryStore<T>` -> `AzureTablesRepositoryStore<T>`
- `IAzureTablesRepositoryAsync<T>` -> `AzureTablesRepositoryAsync<T>`
- `IBaseRepositoryAsync<T>` -> `AzureTablesRepositoryAsync<T>`
- `IBaseRepository<T>` -> `AzureTablesRepositoryAsync<T>`

## Storage Models

- `Envelope`: stores entities as JSON in `Data` column (`PartitionKey`, `RowKey`, `Data`, `Type`).
- `EntityColumns`: stores scalar properties as table columns.

## Azure-Specific Store Methods

`IAzureTablesRepositoryStore<T>` adds:

- `GetByKeysAsync(partitionKey, rowKey)`
- `DeleteByKeysAsync(partitionKey, rowKey)`
- `AddAsync(...)` for insert-only
- `UpdateAsync(...)` for update-only (supports `ETag` and `TableUpdateMode`)
- `GetByPartitionPagedAsync(...)`
- `QueryAsync(...)`

## Entity Mapping + Locator (opt-in)

`AddAzureEntityMapping<T>(...)` enables per-entity table and key mapping.

```csharp
services.AddAzureEntityMapping<PatientClinicalNote>(o =>
{
    o.TableName = "PatientClinicalNotes";
    o.WriteKey = (tenantId, e) => new AzureEntityKey
    {
        PartitionKey = $"TENANT={tenantId:D}|PATIENT={e.PatientId:D}",
        RowKey = e.Id.ToString("N")
    };
    o.CandidatePartitionsForId = (tenantId, id) => null; // optional
    o.EnableIdLocator = true;
});
```

If `EnableIdLocator` is true, register an `IEntityLocator` implementation.
For local/dev, `AddInMemoryEntityLocator()` is available.

## Partition Key Strategy

Per-entity strategy is supported.

Built-in helpers:

```csharp
services.UseGlobalPartitionKey<TimeZoneEntity>();
services.UseTenantPartitionKey<BusinessAddressEntity>();
services.UseTenantHashBucketPartitionKey<UserMessage>(16);
```

Custom strategy:

```csharp
services.AddAzureTablePartitionKeyStrategy<UserMessage>(
    AzureTablePartitionKeyStrategies.Create<UserMessage>(
        writePartition: (tenantId, entity) => $"{entity.TenantId:N}|{entity.UserId:N}",
        idCandidates: (tenantId, id) => null,
        getAllPartitions: tenantId => null));
```

If `idCandidates` is null, point reads/deletes by `Id` fall back to row-key scan.
