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
