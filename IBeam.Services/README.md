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
- custom service operation execution:
  - `IBeamOperationAttribute`
  - `IBeamAuditActionAttribute`
  - `IBeamRequiresPermissionAttribute`
  - `IServiceOperationExecutor`
  - `ServiceOperationExecutionOptions`

## Custom Service Operations

Base CRUD methods already apply service operation authorization and audit logging. Custom service methods can use `IServiceOperationExecutor` to get the same behavior.

```csharp
[IBeamOperation("patients")]
public sealed class PatientService
{
    private readonly IServiceOperationExecutor _operations;

    public PatientService(IServiceOperationExecutor operations)
    {
        _operations = operations;
    }

    [IBeamOperation("patients.discharge")]
    public Task DischargeAsync(Guid patientId, CancellationToken ct = default)
    {
        return _operations.ExecuteAsync(
            this,
            async token =>
            {
                // Custom business rules and repository/service calls live here.
                await Task.CompletedTask;
            },
            new ServiceOperationExecutionOptions
            {
                EntityId = patientId
            },
            ct);
    }
}
```

The executor resolves the operation name from the method attribute, checks service-operation authorization when configured, writes audit transactions when auditing is enabled, captures request/actor/tenant context, and records success/failure metadata for debugging.

Use `ServiceOperationExecutionOptions.OriginalData` and `TransformedData` when the custom method changes data and you can provide before/after entity snapshots. Prefer database entity shapes for those snapshots, not decorated outbound DTOs.

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
