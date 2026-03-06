# Changelog

## 2.0.4 - 2026-03-06

### Added
- Service operation policy framework in `IBeam.Services`:
  - `ServiceOperation` enum
  - `ServiceOperationPolicyAttribute`
  - `ServicePolicyOptions`
  - `IServiceOperationPolicyResolver` + default resolver
  - `AddIBeamServicePolicies(...)` DI registration extension

### Changed
- `BaseServiceAsync<TEntity, TModel>` and `BaseService<TEntity, TModel>` now resolve operation access using policy precedence:
  1. Service class attributes (`[ServiceOperationPolicy(...)]`)
  2. Configured policy options (`IBeam:Services:Policies`)
  3. Existing in-class `Allow*` defaults (fallback)

### Documentation
- Updated `IBeam.Services` docs:
  - `README.core.md`
  - `README.abstractions.md`
- Added policy configuration examples and precedence guidance.

### Migration Notes
- No breaking change required for existing services.
- Existing `Allow*` overrides continue to work when no attribute/config policy is set.
- To opt into config policies, use:

```json
{
  "IBeam": {
    "Services": {
      "Policies": {
        "Services": {
          "YourServiceName": {
            "GetAll": true,
            "Delete": false
          }
        }
      }
    }
  }
}
```

- Attribute policies override config values when both are present.
