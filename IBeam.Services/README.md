# IBeam.Services

Core service-layer abstractions and base implementations for IBeam.

## Narrative Introduction

This package sits between application workflows and repository providers. It standardizes service behavior with reusable base classes, mapping abstractions, auditing hooks, and policy-based operation controls so teams can implement domain services consistently.

## Service Ownership

Services are the home for application behavior. A controller should be a transport adapter: accept HTTP input, call a service, and shape the HTTP response. Business rules, policy checks, validation decisions, auditing/logging, and expected error classification belong in the service layer.

Expected failures should be raised by services with user-safe messages so controllers can return friendly responses. Examples include validation failures, disabled operations, missing records, authorization/policy failures, and domain conflicts.

Unexpected failures and system-level errors should bubble out to the API exception pipeline. They should not expose internal details to the caller. When an `IApiErrorSink` is configured, the API layer persists those operational details to the system error store; with the Azure Table provider, that is the `SystemErrors` table.

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
