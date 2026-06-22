# Changelog

## 2.0.67 - 2026-06-22

### Fixed
- Fixed API credential key parsing when the random base64url secret begins with `_`, which could cause valid API keys to fail authentication intermittently.

### Tests
- Added a regression test that verifies API credential parsing preserves leading-underscore secrets.

## 2.0.66 - 2026-06-22

### Added
- Tenant API credential framework across Identity packages:
  - API credential contracts, models, options, validators, principal factory, service, and authenticator.
  - API-key authentication scheme for `X-API-Key` and `Authorization: ApiKey ...`.
  - API credential management endpoints in `IBeam.Identity.Api`.
  - API credential introspection endpoint for trusted internal validation workflows.
  - Azure Table `ApiCredentials` store and schema registration.
- Tests for API credential creation, hash-only storage, authentication, role claim emission, revocation, invalid hash handling, unsafe role denial, and management authorization.

### Documentation
- Expanded root README with API credential endpoints, authentication headers, emitted claims, and role-rule guidance for services/APIs.
- Updated Azure Table identity README with the `ApiCredentials` table.

## 2.0.12 - 2026-06-22

### Changed
- Azure Table tenant roles now default to the `Roles` table instead of `TenantRoles`.
- The `TenantRolesTableName` option is still available so existing deployments can keep using `TenantRoles` or another configured table name.

### Documentation
- Added tenant role endpoint guidance to the root README.
- Added API credential implementation prompt guidance for IBeam-based applications.
- Updated Azure Table identity README table-set documentation to reference the `Roles` default.

### Validation
- `dotnet build IBeam.Identity.Repositories.AzureTable\IBeam.Identity.Repositories.AzureTable.csproj --no-restore` passed.

## 2.0.11 - 2026-05-14

### Added
- Permission catalog + mapping management surface in identity:
  - `IPermissionCatalogProvider` + `ExposedPermission`
  - `PermissionCatalogProvider` (discovers permissions from attributes + configuration)
  - `PermissionCatalogBuilder` + `AddIBeamIdentityPermissionCatalog(...)`
  - `PermissionMappingsController` endpoints in `IBeam.Identity.Api`
- Role management options model and wiring:
  - `RoleManagementOptions` (`IBeam:Identity:RoleManagement`)
  - role mutation gates in `RolesController`

### Changed
- OTP auto-provision policy is now controlled in core OTP flow (not app wrappers) by:
  - `IBeam:Identity:Otp:AllowAutoProvisionForUnknownUser`
- `StartOtp` behavior:
  - when `true`, unknown destinations are allowed
  - when `false`, unknown destinations are blocked with `Unauthorized`
  - blocked action audit event: `auth.startotp.blocked_unknown_user`
- `CompleteOtp` behavior:
  - when `true`, unknown user flow may auto-provision on successful OTP verify
  - when `false`, any auto-provision path is blocked with `Unauthorized`
  - blocked action audit event: `auth.completeotp.blocked_auto_provision`
- OTP auto-provision defaults when setting is omitted:
  - `Development`: `true`
  - `Test` and `Production`: `false`
  - explicit config override always wins, including env var
    - `IBeam__Identity__Otp__AllowAutoProvisionForUnknownUser=true|false`
- Permission configuration options extended with catalog entries:
  - `IBeam:Identity:PermissionAccess:Catalog`

### Tests
- Added OTP behavior tests for blocked unknown-user start/complete flows.
- Added OTP options default/override tests for environment-sensitive defaulting.
- Added API/service tests around permission catalog/mappings and role-management gates.

### Validation
- `dotnet test IBeam.Tests.Identity.Services/IBeam.Tests.Identity.Services.csproj` could not run in this environment due restricted outbound NuGet access (`NU1301` to `api.nuget.org` / `nuget.pkg.github.com`).

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
