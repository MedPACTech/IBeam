# IBeam.Repositories

`IBeam.Repositories` contains the core repository contracts and base implementations used by IBeam services.

```powershell
dotnet add package IBeam.Repositories
```

## When To Use This

- You are building an IBeam service and need a clean persistence boundary.
- You want CRUD, archive, soft-delete, and tenant-aware conventions.
- You want services to depend on repository abstractions instead of a specific database provider.
- You are implementing a new repository provider for Azure Tables, SQL, OrmLite, EF, or another store.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Entity contracts | `IEntity`, `ITenantEntity`, `IArchivableEntity`, `IDeletableEntity`, `IAllowHardDelete` | Defines common persistence fields and behavior. |
| Repository contracts | `IRepositoryStore<T>`, `IBaseRepository<T>`, `IBaseRepositoryAsync<T>`, `IArchivableRepositoryAsync<T>` | Standard CRUD/archive interfaces used by service layers. |
| Base repositories | `BaseRepository<T>`, `BaseRepositoryAsync<T>` | Shared repository behavior over a backing `IRepositoryStore<T>`. |
| Tenant context | `ITenantContext`, `TenantContext` | Carries tenant identity into repository calls. |
| Options | `RepositoryOptions` | Controls cache, ID generation, and soft-delete behavior. |
| Errors | repository exception types | Provides consistent validation/system failure behavior. |
| Batching | batch action models | Gives providers a common way to submit grouped changes. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

Repositories only manage one entity type. They should not call other repositories, enforce business permissions, write audit logs directly, or hydrate DTOs with external lookup data. That work belongs in the service layer.

## Code Example

```csharp
public sealed class Product : IEntity, ITenantEntity, IArchivableEntity, IDeletableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class ProductService
{
    private readonly IBaseRepositoryAsync<Product> _products;

    public ProductService(IBaseRepositoryAsync<Product> products)
    {
        _products = products;
    }

    public Task<Product?> GetAsync(Guid productId, CancellationToken ct)
        => _products.GetByIdAsync(productId, ct: ct);
}
```

## Repository Options

| Setting | Default | Purpose |
|---|---:|---|
| `EnableCache` | `true` | Allows repository implementations to use cache where supported. |
| `IdGeneratedByRepository` | `false` | When `true`, repositories may generate `Guid` IDs for new entities. |
| `DisableSoftDelete` | `false` | When `true`, delete behavior may hard-delete instead of using `IsDeleted`. |

## Data Storage

This package does not create tables, containers, or database schema. Physical storage comes from provider packages.

| Provider Package | Storage Type | Notes |
|---|---|---|
| `IBeam.Repositories.AzureTables` | Azure Table Storage | Generic repository table provider. |
| `IBeam.Repositories.OrmLite` | Relational databases through ServiceStack OrmLite | SQL-backed repository provider. |

## Service Operations, Auditing, And Permissions

Repositories are below the service boundary. Permission checks and audit logging should wrap service calls, not raw repository calls. This keeps the service layer as the all-knowing gatekeeper for rules, logging, errors, and orchestration.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)

Agents should keep repository code entity-focused and avoid circular service/repository dependencies.

## Version Notes

- Targets `net10.0`.
- Package version is assigned by the repository release workflow.
