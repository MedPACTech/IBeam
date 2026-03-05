# IBeam.Repositories.AzureTables

Azure Table Storage provider for `IBeam.Repositories`.

## Startup Registration

```csharp
builder.Services.ConfigureIBeamAzureTables(builder.Configuration);
builder.Services.AddIBeamAzureTablesRepositories();
```

Programmatic override is also supported:

```csharp
builder.Services.ConfigureIBeamAzureTables(options =>
{
    options.ConnectionString = "UseDevelopmentStorage=true";
    options.TableNamePrefix = string.Empty;
    options.CreateTablesIfNotExists = true;
    options.StorageModel = AzureTableStorageModel.Envelope;
    options.GuidKeyFormat = "N"; // "N" or "D"
    options.EnableLegacyGuidKeyFallbackReads = false;
});
```

## Configuration

Section: `IBeam:Repositories:AzureTables`

```json
{
  "IBeam": {
    "Repositories": {
      "AzureTables": {
        "ConnectionString": "UseDevelopmentStorage=true",
        "TableNamePrefix": "",
        "CreateTablesIfNotExists": true,
        "StorageModel": "Envelope",
        "GuidKeyFormat": "N",
        "EnableLegacyGuidKeyFallbackReads": false
      }
    }
  }
}
```

`StorageModel` values:
- `Envelope`
- `EntityColumns`

`GuidKeyFormat` values:
- `N` (default)
- `D`

## Connection String Resolution

`ConfigureIBeamAzureTables(configuration)` resolves connection string in this order:

1. `IBeam:Repositories:AzureTables:ConnectionString`
2. `IBeam:AzureTables`
3. `IBeam:ConnectionString`
4. `ConnectionStrings:AzureTables`
5. `ConnectionStrings:AzureTable`
6. `ConnectionStrings:AzureStorage`
7. `ConnectionStrings:IBeam`
8. `ConnectionStrings:DefaultConnection`

## Global GUID Key Format

`GuidKeyFormat` controls IBeam-generated GUID key strings across:
- default row-key generation
- id-locator identifiers
- fallback read key candidates

Set `EnableLegacyGuidKeyFallbackReads = true` during migration if existing data uses the other format.
Example: switch to `N`, enable fallback reads, backfill/write-forward, then disable fallback once old records are migrated.

## Storage Model Overrides

Global default is from configuration. Per-entity override:

```csharp
[AzureTableStorageModel(AzureTableStorageModel.EntityColumns)]
public sealed class MyEntity : IEntity
{
    public Guid Id { get; set; }
    public bool IsDeleted { get; set; }
}
```

If attribute is present, it overrides global `StorageModel` for that entity type.

## Envelope Hybrid Projection

When using `Envelope`, full entity JSON is written to `Data`. You can also project selected properties into top-level columns:

```csharp
public sealed class MyEntity : IEntity
{
    public Guid Id { get; set; }
    public bool IsDeleted { get; set; }

    [AzureTableProjectedColumn("SearchName")]
    public string Name { get; set; } = string.Empty;
}
```

This stores both:
- `Data` (full JSON payload)
- `SearchName` (queryable column)

## Entity Mapping + Locator

Per-entity mapping:

```csharp
builder.Services.AddAzureEntityMapping<PatientClinicalNote>(o =>
{
    o.TableName = "PatientClinicalNotes";
    o.WriteKey = (tenantId, e) => new AzureEntityKey
    {
        PartitionKey = $"TENANT={tenantId:D}|PATIENT={e.PatientId:D}",
        RowKey = e.Id.ToString("N")
    };
    o.EnableIdLocator = true;
});
```

If `EnableIdLocator = true`, register an `IEntityLocator` (or use `AddInMemoryEntityLocator()` for dev/test).

## Key Resolver for App Services

Use `IAzureEntityKeyResolver<T>` to compute keys with the same mapping logic used by repository writes:

```csharp
public sealed class NoteService
{
    private readonly IAzureEntityKeyResolver<PatientClinicalNote> _keys;

    public NoteService(IAzureEntityKeyResolver<PatientClinicalNote> keys)
    {
        _keys = keys;
    }

    public AzureEntityKey Resolve(Guid tenantId, PatientClinicalNote note)
        => _keys.ResolveWriteKey(tenantId, note);
}
```

## What DI Registers

- `IRepositoryStore<T>` -> `AzureTablesRepositoryStore<T>`
- `IAzureTablesRepositoryStore<T>` -> `AzureTablesRepositoryStore<T>`
- `IAzureTablesRepositoryAsync<T>` -> `AzureTablesRepositoryAsync<T>`
- `IBaseRepositoryAsync<T>` -> `AzureTablesRepositoryAsync<T>`
- `IBaseRepository<T>` -> `AzureTablesRepositoryAsync<T>`

## Azure-Specific Methods

`IAzureTablesRepositoryStore<T>` adds:
- `GetByKeysAsync(partitionKey, rowKey)`
- `DeleteByKeysAsync(partitionKey, rowKey)`
- `SubmitBatchAsync(partitionKey, actions)`
- `SubmitBatchesAsync(partitionKey, actions, chunkSize)`
- `GetByPartitionPagedAsync(...)`
- `QueryAsync(...)`

Batch constraints:
- single partition per transaction
- max 100 actions
- atomic commit/rollback

`IAzureTablesRepositoryAsync<T>` also exposes `GetByIdAsync(Guid id, CancellationToken ct = default)` for common locator-backed flows.
