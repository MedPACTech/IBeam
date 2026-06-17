# IBeam.Services

Core base-service framework over `IBeam.Repositories` with model/entity mapping, auditing hooks, and operation policy controls.

## What This Package Provides

- `BaseService<TEntity, TModel>` (sync)
- `BaseServiceAsync<TEntity, TModel>` (async)
- `IModelMapper<TEntity, TModel>` abstraction
- Audit abstractions (`IAuditService*`, `IEntityAuditService*`)
- Service operation policy controls via:
  - configuration options
  - attributes
  - legacy `Allow*` flags fallback

## Service Responsibilities

Keep business rules in services. Base services and derived domain services should own operation policy checks, validation, business-rule enforcement, auditing/logging hooks, and the decision to throw an expected, user-safe exception versus allowing an unexpected system failure to bubble.

Controllers should not duplicate service rules or translate internal exceptions into ad hoc messages. Expected service errors can be returned to callers with friendly messages. Unexpected or non-user-friendly errors should flow to centralized API exception handling, where they can be logged and persisted to the configured system error sink, such as the Azure Table `SystemErrors` table.

## Service Policies

Service operation permissions can be controlled in three layers:

1. Attribute override on service class (`ServiceOperationPolicyAttribute`)
2. Configuration options (`IBeam:Services:Policies`)
3. Legacy `Allow*` booleans in the service/base class (fallback)

Effective rank:

`Attribute > Config/Options > Allow* fallback`

## appsettings Example

```json
{
  "IBeam": {
    "Services": {
      "Policies": {
        "Services": {
          "PatientService": {
            "GetById": true,
            "GetByIds": false,
            "GetAll": true,
            "GetAllWithArchived": false,
            "Save": true,
            "SaveAll": true,
            "Archive": true,
            "Unarchive": true,
            "Delete": false
          }
        }
      }
    }
  }
}
```

Only set properties you want to override. Unset operations use service defaults.

## Startup Registration

```csharp
builder.Services.AddIBeamServicePolicies(options =>
{
    options.Services["PatientService"] = new ServiceOperationAccessOptions
    {
        GetAll = true,
        Delete = false
    };
});
```

You can also bind from `IConfiguration` manually and pass into `AddIBeamServicePolicies(...)`.

## Attribute Example

```csharp
[ServiceOperationPolicy(ServiceOperation.Delete, false)]
[ServiceOperationPolicy(ServiceOperation.GetAll, true)]
public sealed class PatientService : BaseServiceAsync<PatientEntity, PatientModel>
{
    // ...
}
```

## Mapping

`IModelMapper<TEntity, TModel>` is provider-agnostic.  
Use `IBeam.Services.AutoMapper` or a custom mapper implementation.
