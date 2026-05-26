# IBeam.Services

Core service-layer abstractions and base implementations for IBeam.

## Narrative Introduction

This package sits between application workflows and repository providers. It standardizes service behavior with reusable base classes, mapping abstractions, auditing hooks, and policy-based operation controls so teams can implement domain services consistently.

## Features and Components

- abstractions in `IBeam.Services.Abstractions`:
  - `IBaseService<TEntity,TModel>`
  - `IBaseServiceAsync<TEntity,TModel>`
  - `IModelMapper<TEntity,TModel>`
  - audit interfaces (`IAuditService*`, `IEntityAuditService*`)
- core implementations in `IBeam.Services.Core`:
  - `BaseService<TEntity,TModel>`
  - `BaseServiceAsync<TEntity,TModel>`
- service operation policy system:
  - `ServiceOperationPolicyAttribute`
  - `ServicePolicyOptions`
  - `IServiceOperationPolicyResolver`
  - `AddIBeamServicePolicies(...)`

## Dependencies

- Internal packages:
  - `IBeam.Repositories`
  - `IBeam.Utilities`
- External packages:
  - `AutoMapper`
  - `Microsoft.Extensions.Options`

## Additional Docs

- `README.abstractions.md`: contract quick reference
- `README.core.md`: policy and configuration details
