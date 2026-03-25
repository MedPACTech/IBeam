# Changelog

## 2.0.10 - 2026-03-24

### Added
- Open-source governance and policy docs:
  - `LICENSE` (Apache-2.0)
  - `LICENSE-COMMERCIAL.md` (draft commercial add-on terms)
  - `NOTICE`
  - `CONTRIBUTING.md`
  - `CODE_OF_CONDUCT.md`
  - `SECURITY.md`
- Open-source program docs:
  - `docs/open-source-checklist.md`
  - `docs/github-repo-profile.md`
  - `docs/release-strategy.md`
  - `docs/release-notes-template.md`
  - `docs/licensing.md`
  - `docs/landing-page-plan.md`
- CI/CD workflow scaffolding:
  - `.github/workflows/ci.yml`
  - `.github/workflows/publish-prerelease-gpr.yml`
  - `.github/workflows/publish-nuget-release.yml`
- NuGet packaging baseline enhancements:
  - shared package metadata defaults in `Directory.Build.props`
  - shared package icon (`docs/assets/ibeam-icon.png`) for packable projects
- New storage package family and tests:
  - `IBeam.Storage.Abstractions`
  - `IBeam.Storage.AzureBlobs`
  - `IBeam.Storage.FileSystem`
  - `IBeam.Storage.S3`
  - storage unit tests for AzureBlobs/FileSystem/S3
- New optional service logging package and tests:
  - `IBeam.Services.Logging`
  - repository/logger audit sink tests

### Changed
- Root `README.md` rewritten with:
  - framework narrative introduction
  - extension/plugin architecture messaging
  - OSS and contribution guidance
  - badges for CI, NuGet, license, and releases
- `IBeam.Services` and `IBeam.Services.AutoMapper` upgraded to `AutoMapper 16.1.1`.
- `IBeam.Storage.S3` upgraded to `AWSSDK.S3 4.0.19.1`.
- OTP persistence hardening and channel propagation:
  - fixed Azure Table OTP `CreatedAt` out-of-range write path
  - persisted OTP channel end-to-end (`SenderChannel`) in challenge storage mapping

### Validation
- `dotnet build IBeam.sln -c Release` passed.
- `dotnet test IBeam.sln -c Release` passed.
- `dotnet list IBeam.sln package --vulnerable --include-transitive` reports no vulnerable packages.
- `dotnet pack IBeam.sln -c Release -o artifacts/packages` succeeded.
- Local package smoke-install validated via `_tmp_pkg_smoke` project against packed artifacts.

### Operational Notes
- NuGet.org publish workflow is ready, but direct publish requires `NUGET_API_KEY`.
- GitHub repository About/topics settings are prepared in `docs/github-repo-profile.md` and require manual UI apply.

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
