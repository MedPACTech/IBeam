# IBeam.Services.Abstractions

Contracts for the IBeam service layer.

## Key Interfaces

- `IBaseService<TEntity, TModel>`
- `IBaseServiceAsync<TEntity, TModel>`
- `IModelMapper<TEntity, TModel>`
- `IAuditService` / `IAuditServiceAsync`
- `IEntityAuditService<TEntity>` / `IEntityAuditServiceAsync<TEntity>`

## Policy Controls

Operation permission controls are available via:

- `ServiceOperationPolicyAttribute`
- `ServicePolicyOptions` (`IBeam:Services:Policies`)
- `IServiceOperationPolicyResolver`

See `README.core.md` for full configuration and startup examples.
