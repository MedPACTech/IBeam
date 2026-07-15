# IBeam Service Logging and Audit

IBeam services should keep logging and auditing close to the service layer because the service is the gatekeeper for business rules, repository writes, permission checks, and error handling.

## Logging Concepts

IBeam treats these as related but separate concerns:

| Concern | Purpose | Recommended tool |
|---|---|---|
| Diagnostic logging | Runtime messages, warnings, exceptions, infrastructure details. | `ILogger<T>` |
| Entity change audit | Durable record of adds, updates, deletes, archives, and unarchives. | `IAuditTrailSink` / IBeam audit logging |
| Operation identity | Stable service action names such as `patients.discharge` or `pricing.save`. | `IBeamOperationAttribute`, config, or convention |
| Permission mapping | Role/grant checks for operation names. | IBeam AccessControl permission maps |

## Default Behavior

Apps should not need to map every service to get audit coverage. When service auditing is enabled, IBeam audits write operations by convention:

- `Create`
- `Update`
- `Delete`
- `Archive`
- `Unarchive`

Read/query operations are not audited by default. Select rollups exist, but they are optional and must be explicitly enabled.

Minimal configuration:

```json
{
  "IBeam": {
    "Services": {
      "Audit": {
        "Enabled": true
      }
    }
  }
}
```

With this configuration, a service using `BaseService` or `BaseServiceAsync` writes audit transactions for normal data changes.

## Audit Entry Shape

An entity change audit entry should capture:

| Field | Purpose |
|---|---|
| `OccurredUtc` | When the change happened. |
| `ServiceName` | Service type that performed the action, for example `ProductsService`. |
| `EntityName` | Logical entity name, for example `products`. |
| `Operation` | Framework operation, such as `Create`, `Update`, `Archive`, or `Delete`. |
| `Action` | Stable operation name, such as `products.create` or `patients.discharge`. |
| `EntityId` | Entity identifier when available. |
| `TenantId` | Tenant context when available. |
| `ActorId` | User/system actor when available. |
| `CorrelationId` | Request/trace identifier when available. |
| `IpAddress` | Caller IP when available. |
| `UserAgent` | Caller user agent when available. |
| `DeviceId` | Optional device identifier when supplied. |
| `BeforeJson` | JSON copy of the database entity before the write. |
| `AfterJson` | JSON copy of the database entity after the write. |

The JSON snapshots should represent the database entity, not the outbound DTO. DTOs may be decorated with external data, which makes them weaker for rollback, investigation, and data repair.

## Configuration

The audit section supports global defaults plus exceptions.

```json
{
  "IBeam": {
    "Services": {
      "Audit": {
        "Enabled": true,
        "DefaultMode": "AuditWrites",
        "CaptureBefore": true,
        "CaptureAfter": true,
        "FailOnAuditError": false,
        "Services": {
          "HighVolumeTelemetryService": {
            "Enabled": false
          },
          "ProductsService": {
            "EntityName": "products",
            "Operations": {
              "Create": {
                "Action": "catalog.products.create"
              },
              "Update": {
                "Action": "catalog.products.update"
              },
              "Delete": {
                "Enabled": false
              }
            }
          }
        }
      }
    }
  }
}
```

Use configuration when:

- a service should opt out of auditing
- an operation should have a public/stable action name
- a noisy operation should be excluded
- before/after snapshots need to be disabled for sensitive or large entities

## Operation Attributes

`IBeamOperationAttribute` is the master attribute. It gives a method or service one stable operation name that can be used by audit, permission maps, logs, docs, and UI.

```csharp
[IBeamOperation("patients.discharge")]
public async Task DischargeAsync(Guid patientId, CancellationToken ct = default)
{
    // custom domain operation
}
```

That single name can feed:

```text
Audit action: patients.discharge
Permission:   patients.discharge
Policy key:   patients.discharge
Log label:    patients.discharge
```

When audit and permission names need to differ, use a specific override:

```csharp
[IBeamOperation("patients.discharge")]
[IBeamAuditAction("patients.discharge.completed")]
[IBeamRequiresPermission("patients.manage-discharge")]
public async Task DischargeAsync(Guid patientId, CancellationToken ct = default)
{
    // custom domain operation
}
```

Recommended resolution order:

1. Specific method attribute, such as `IBeamAuditActionAttribute` or `IBeamRequiresPermissionAttribute`.
2. Method-level `IBeamOperationAttribute`.
3. Specific service class attribute.
4. Service-level `IBeamOperationAttribute`.
5. Configuration override.
6. Convention fallback, such as `products.create`.

## Service-Level CRUD Example

```csharp
public sealed class ProductsService : BaseServiceAsync<ProductEntity, ProductModel>
{
    public ProductsService(
        IBaseRepositoryAsync<ProductEntity> repository,
        IModelMapper<ProductEntity, ProductModel> mapper,
        IAuditTrailSink auditTrailSink,
        IOptionsMonitor<ServiceAuditOptions> auditOptions)
        : base(
            repository,
            mapper,
            auditTrailSink: auditTrailSink,
            auditOptionsMonitor: auditOptions)
    {
        AllowSave = true;
        AllowArchive = true;
        AllowDelete = true;
    }
}
```

With audit enabled, `SaveAsync`, `ArchiveAsync`, `UnarchiveAsync`, and `DeleteAsync` write audit transactions automatically.

## Sink Registration

Use the logger sink for development or log-pipeline forwarding:

```csharp
builder.Services.AddIBeamLoggerAuditTrail(builder.Configuration);
builder.Services.AddIBeamHttpContextAuditActor();
```

Use the repository sink when audit events should be persisted through an IBeam repository:

```csharp
builder.Services.AddIBeamRepositoryAuditTrail(builder.Configuration);
builder.Services.AddIBeamHttpContextAuditActor();
```

Use the Azure Table sink when audit events and system logs should be written to `IBeamSystemLogs` without depending on `IBeam.Identity`:

```csharp
builder.Services.AddIBeamAzureTableSystemLogs(builder.Configuration);
builder.Services.AddIBeamHttpContextAuditActor();
```

```json
{
  "IBeam": {
    "Logging": {
      "AzureTable": {
        "StorageConnectionString": "<connection-string>",
        "TablePrefix": "IBeam",
        "TableName": "SystemLogs"
      }
    }
  }
}
```

Teams can also implement their own sink:

```csharp
public sealed class MyAuditTrailSink : IAuditTrailSink
{
    public Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
    {
        // persist to Azure Table, SQL, blob storage, SIEM, etc.
        return Task.CompletedTask;
    }

    public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
```

## ILogger Guidance

`ILogger<T>` should remain the primary diagnostic logging tool. It is ideal for errors, warnings, timing, and infrastructure behavior.

Entity change auditing should use `IAuditTrailSink` because audit records need stable business fields, before/after JSON, user/tenant context, and predictable storage.

The recommended pattern is:

```text
ILogger<T>        -> diagnostics
IAuditTrailSink   -> durable entity change history
Permission maps   -> authorization for named service operations
```

## IBeamSystemLogs Direction

The built-in durable table is `IBeamSystemLogs` after the configured table prefix is applied. The Azure Table implementation lives in `IBeam.Services.Logging`, so teams can use it without adopting `IBeam.Identity`.

Recommended Azure Table keys:

```text
PartitionKey = TENANT|{tenantId}|DAY|yyyyMMdd
RowKey       = {HHmmssfffffff}|{eventId}
```

For non-tenant logs:

```text
PartitionKey = SYSTEM|DAY|yyyyMMdd
RowKey       = {HHmmssfffffff}|{eventId}
```

This keeps tenant/day queries practical and keeps the table useful for operational review, rollback analysis, and infrastructure monitoring.
