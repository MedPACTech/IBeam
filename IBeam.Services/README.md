# IBeam.Services

`IBeam.Services` contains the core service-layer abstractions, base CRUD services, operation metadata, audit hooks, and policy execution primitives used across IBeam.

```powershell
dotnet add package IBeam.Services
```

## When To Use This

- You are building an IBeam service around one entity.
- You want base CRUD behavior with override points.
- You need service-operation tags such as `[IBeamOperation("patients.discharge")]`.
- You want permissions, auditing, validation, and error behavior to stay in the service layer.
- You are using your own authentication system but still want IBeam service policy and audit patterns.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Base services | `BaseService<TEntity,TModel>`, `BaseServiceAsync<TEntity,TModel>` | Reusable CRUD framework over repositories and model mappers. |
| Service contracts | `IBaseService<TEntity,TModel>`, `IBaseServiceAsync<TEntity,TModel>` | Common service operations for apps and APIs. |
| Mapping | `IModelMapper<TEntity,TModel>` | Keeps entity/model conversion behind a small abstraction. |
| Operation metadata | `IBeamOperationAttribute`, `IBeamAuditActionAttribute`, `IBeamRequiresPermissionAttribute` | Names service calls for authorization, auditing, and debugging. |
| Operation execution | `IServiceOperationExecutor`, `ServiceOperationExecutionOptions` | Wraps custom methods with the same policy/audit behavior as base CRUD methods. |
| Policy resolution | `IServiceOperationPolicyResolver`, `ServicePolicyOptions` | Enables or disables CRUD operations per service. |
| Audit hooks | `IAuditTrailSink`, `IAuditActorProvider`, `IAuditRequestContextProvider` | Captures who changed what, where, and when. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

The service layer is the gatekeeper in IBeam. Controllers should bind requests and return responses. Repositories should persist one entity. Services should own business rules, permission checks, logging/auditing decisions, validation, error classification, and cross-service coordination.

## Base CRUD Pattern

```csharp
[IBeamOperation("products")]
public sealed class ProductsService : BaseServiceAsync<ProductEntity, ProductDto>
{
    public ProductsService(
        IBaseRepositoryAsync<ProductEntity> repository,
        IModelMapper<ProductEntity, ProductDto> mapper)
        : base(repository, mapper)
    {
    }

    protected override Task ValidateSaveAsync(ProductEntity entity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            throw new InvalidOperationException("Product name is required.");
        }

        return Task.CompletedTask;
    }
}
```

Base CRUD methods already flow through IBeam service policy and audit behavior when configured.

## Custom Service Operations

Custom methods should use `IServiceOperationExecutor` so auditing, permission evaluation, request context, actor context, and failure metadata are consistent.

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
                // Business rules and repository/service calls live here.
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

Use `OriginalData` and `TransformedData` when a custom method changes data and you can provide before/after entity snapshots. Prefer database entity shapes, not decorated outbound DTOs.

## Service Policy Configuration

Configuration section: `IBeam:Services:Policies`

```json
{
  "IBeam": {
    "Services": {
      "Policies": {
        "Services": {
          "ProductsService": {
            "GetAll": true,
            "Save": true,
            "Delete": false
          }
        }
      }
    }
  }
}
```

Use service policies to disable risky operations without changing code, such as blocking deletes during an incident.

## Audit Configuration

Configuration section: `IBeam:Services:Audit`

```json
{
  "IBeam": {
    "Services": {
      "Audit": {
        "Enabled": true,
        "DefaultMode": "AuditWrites",
        "EnableSelectAudits": false,
        "SelectMode": "DailyRollup",
        "CaptureBefore": true,
        "CaptureAfter": true,
        "FailOnAuditError": false,
        "Services": {
          "ProductsService": {
            "EntityName": "products",
            "Operations": {
              "Save": {
                "Action": "products.save"
              },
              "Delete": {
                "Action": "products.delete"
              }
            }
          }
        }
      }
    }
  }
}
```

The audit hierarchy is broad default first, then service override, then operation override. That lets teams audit most services by default while turning specific services or methods off.

## Data Storage

This package does not create audit tables by itself. It defines the audit contracts and execution hooks. Use `IBeam.Services.Logging` to write audit transactions to `ILogger`, repositories, or Azure Table Storage.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Roles, permissions, and grants: [`../docs/roles-permissions-and-grants.md`](../docs/roles-permissions-and-grants.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents should make the service method the named operation boundary and keep API/repository layers thin.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Custom method is not audited | Method bypasses `IServiceOperationExecutor` | Wrap the custom method body with the executor. |
| Operation name is missing | `[IBeamOperation]` not applied | Add class and/or method operation attributes. |
| Delete/save is unexpectedly blocked | Service policy disabled the operation | Check `IBeam:Services:Policies`. |
| Audit contains decorated DTOs | API response model was passed as audit data | Pass entity/database-shaped snapshots instead. |

## Version Notes

- Targets `net10.0`.
- Designed for both IBeam Identity and bring-your-own authentication.
- Package version is assigned by the repository release workflow.
