# IBeam.Services.AutoMapper

`IBeam.Services.AutoMapper` bridges IBeam's `IModelMapper<TEntity,TModel>` abstraction to AutoMapper.

```powershell
dotnet add package IBeam.Services.AutoMapper
```

## When To Use This

- You use `IBeam.Services` base services and want AutoMapper for entity/model mapping.
- You already maintain AutoMapper profiles in your application.
- You want services to depend on `IModelMapper<TEntity,TModel>` instead of AutoMapper directly.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Mapper adapter | `AutoMapperModelMapper<TEntity,TModel>` | Implements IBeam model mapping through AutoMapper. |
| DI | `AddIBeamAutoMapper(params Assembly[] profileAssemblies)` | Registers AutoMapper and the IBeam mapper abstraction. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

Mapping supports the service boundary. Entities should represent persistence shape. Models/DTOs can represent the API or service-facing shape. Business rules should not be hidden in AutoMapper profiles.

## Quick Start

```csharp
using IBeam.Services.AutoMapper;

builder.Services.AddIBeamAutoMapper(typeof(ProductMappingProfile).Assembly);
```

Example profile:

```csharp
public sealed class ProductMappingProfile : Profile
{
    public ProductMappingProfile()
    {
        CreateMap<ProductEntity, ProductDto>().ReverseMap();
    }
}
```

Example service registration:

```csharp
builder.Services.AddScoped<ProductsService>();
```

The base service can then receive `IModelMapper<ProductEntity, ProductDto>`.

## Data Storage

This package does not create tables or repositories. It only maps objects.

## Service Operations, Auditing, And Permissions

Mapping is not an operation boundary. Service methods should still be tagged with `[IBeamOperation]` and should pass database-shaped before/after data into audit calls when needed.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)

Agents should keep mapping straightforward and put validation/rules in services.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Mapper fails at runtime | Profile assembly was not registered | Pass the profile assembly to `AddIBeamAutoMapper`. |
| Business behavior is hard to debug | Rules live in mapping profiles | Move rules into the service layer. |
| Audit data looks like API output | DTO was audited instead of entity | Audit entity-shaped before/after data. |

## Version Notes

- Targets `net10.0`.
- Uses AutoMapper.
- Package version is assigned by the repository release workflow.
