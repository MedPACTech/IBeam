# Changelog

## 2.14.0 - 2026-09-28

Three security fixes with one theme: **configuration now means what it says.** All three are breaking for apps that configure the affected settings; apps that configure nothing are unaffected.

### Security

- Configuration can now **narrow** `IBeam:Identity:AccessControl` role and permission lists, not only widen them. `IConfiguration.Bind` adds to a collection rather than replacing it, so every list on `IBeamAccessControlOptions` — which defines who is an owner or an administrator — kept its built-in names no matter what an app configured. An app that set `AdminRoleNames` to `["RegionalAdmin"]` in order to restrict administrator access still treated anyone holding a role named `Administrator` or `Admin` as an administrator, with no warning that its configuration had not taken effect.
- OAuth clients configured with explicit `AllowedGrantTypes` no longer silently also allow `authorization_code`. A client registered for the device-code grant alone previously permitted both.
- **Tenant invite links are restricted to an allowlist of origins.** `DefaultTenantInviteUrlBuilder` built the "Accept invitation" link from whatever `RedirectUrl` the caller supplied, with no validation, so anyone who could create an invite could send a genuine, app-branded invitation whose button opened a site of their choosing. Separately and independently, `DefaultTenantInviteMessageFactory` copied caller-supplied `Metadata` over its own values, so `metadata: { "inviteUrl": "https://evil.example" }` replaced the link even when the redirect URL was valid — and because that dictionary was case-sensitive, `InviteUrl` reached the template model alongside `inviteUrl`. Both paths are closed.

### Changed

- **Breaking for apps that configure access-control lists.** If your app both configures a list under `IBeam:Identity:AccessControl` and relies on IBeam's built-in values still being present, add those values to your configuration explicitly. The lists: `OwnerRoleNames`, `AdminRoleNames`, `ApplicationRoleNames`, `TenantManagementPermissionNames`, `TenantUserManagementPermissionNames`, `TenantRoleManagementPermissionNames`, `TenantAccessControlManagementPermissionNames`, `ApiCredentialManagementPermissionNames`, `OAuthClientManagementPermissionNames`, `AuthAttemptManagementRoleNames`, `AuthAttemptManagementPermissionNames`, `AccessLevels`.
- **Breaking for apps that configure OAuth client grant types.** A configured `AllowedGrantTypes` now means exactly those grants. An app relying on the implicit `authorization_code` alongside its configured grants must list it.
- **Breaking for apps that send tenant invites.** Configure `IBeam:Identity:Invites:Links:DefaultAcceptUrl` with your invite accept page, and list your front-end origins under `AllowedOrigins`. The previous hardcoded `https://localhost:3000/invites/accept` fallback is gone — an app that sends an invite without configuring a destination now fails with a message naming the setting.
- An OAuth client that specifies no grant types in configuration continues to default to `authorization_code`, per RFC 7591 §2. Registering a client programmatically or through the administration API with an explicitly empty grant list is still rejected — configuration cannot express an empty list, but a caller can, and meaning it should not silently earn a grant.

### Added — invite link control

```jsonc
"IBeam:Identity:Invites:Links": {
  "AllowedOrigins": [ "https://app.example.com" ],  // exact scheme://host[:port], no wildcards
  "DefaultAcceptUrl": "https://app.example.com/invites/accept",
  "AcceptPath": "/invites/accept",                  // discards the caller's path on an allowed origin
  "OnDisallowed": "Fallback"                        // or "Reject" to refuse the invite
}
```

Configurable in code as well, which is the point rather than a convenience — `AddIBeamTenantInviteLinks`'s delegate runs *after* configuration binding, so an app can build the allowlist from its own CORS origins rather than repeating them:

```csharp
services.AddIBeamTenantInviteLinks(o =>
{
    foreach (var origin in myCorsOrigins) o.AllowedOrigins.Add(origin);

    // For rules a list cannot express — per-tenant custom domains, preview environments.
    // Consulted only after the allowlist says no, and only for a structurally valid URL.
    o.IsAllowed = (uri, invite) => uri.Host.EndsWith(".preview.example.com");
});
```

`ITenantInviteLinkPolicy` is `TryAddScoped`, so it can be replaced outright. `AllowAnyOrigin()` restores the pre-2.14.0 behaviour deliberately and logs a warning each time it is used.

Do not populate `AllowedOrigins` from a CORS wildcard. An app trusting `*.example-hosting.net` for CORS is trusting every tenant of that host.

### Upgrading

1. **Access control.** Search `appsettings*.json` for `IBeam:Identity:AccessControl`. If you configure any of the listed lists, that list is now exactly what you wrote — add back any IBeam default you were relying on.
2. **OAuth grants.** Search for `AllowedGrantTypes`. If a client configures it and also expects `authorization_code`, add it.
3. **Invites.** If your app sends tenant invites, configure `DefaultAcceptUrl` and `AllowedOrigins` before deploying. This one fails loudly rather than silently, so it will be obvious — but it will be obvious at the moment someone sends an invite.
4. Apps that configure none of the three need no changes.

The direction of the access-control change matters: before this release, configuration could only ever **grant more** access than it appeared to. An app unknowingly relying on the old behaviour will find access narrowing on upgrade rather than widening — a failure that is visible, not silent.

### Why a minor version

Three breaking changes would ordinarily argue for a major bump. Released as a minor because the blast radius is bounded: each one affects only apps that configure the specific setting involved. Anyone who does should read Upgrading rather than treating this as routine.

### Validation

Verified on the released commit (`d128420`), not on a development branch:

- **676 tests passed across 31 suites, 0 failed, 1 skipped.**
- New coverage that binds options from `IConfiguration` and asserts the bound values equal exactly what was configured — the gap that let the first two bugs survive: `AccessControlOptionsConfigurationTests`, `OAuthAuthorizationServerOptionsTests`, `TenantInviteLinkPolicyTests`.
- The invite tests were checked against the unfixed code: reverting the URL builder fails 1, reverting the metadata merge order fails 2. One test gates the wiring specifically, because the policy tests alone would have passed with a correct policy that nothing used.

### Known consumers

- **Bindry** has already adapted to the access-control change (`BIND-0214`), and carries its own app-level invite-link fix (`BIND-0124`) which it can now delete in favour of registering IBeam's policy with its real CORS origins. It is pinned to `IBeamVersion 2.12.2`, so bumping crosses `2.12.3`, `2.13.0`, `2.13.1` and this release together.
- Every other IBeam consumer should be checked against the Upgrading steps. IBeam is the shared identity layer across seven-plus products, and this release touches who counts as an administrator and where invite emails point.

---

**IBM-0068** · **IBM-0072** · **IBM-0073**


## 2.9.45 - 2026-08-10

### Added
- Added provider-neutral public checkout, pending purchases, hosted-checkout gateway contracts, verified webhook normalization, and an optional Stripe adapter.
- Added paid-purchase fulfillment with stable GUID license keys, pre-Identity claim onboarding, one-license seat policies, provider migration, and durable Azure Table commerce stores.
- Added tenant-scoped commerce administration endpoints for redacted inspection, failed-event retry, fulfillment retry, claim rotation, and idempotent manual correction.
- Added end-to-end commerce coverage for anonymous three-seat purchase, payment replay, Identity claim, first-seat assignment, failed payment, cancellation, and provider portability.

### Changed
- Billing purchases now transition idempotently from fulfilled to claimed and bind to the verified tenant and buyer.
- Multi-user licenses use three as the minimum total seat count while individual licenses remain one license with one seat.
- Provider migrations preserve the internal subscription, license key, assignments, and binding history while allowing one active provider binding.

### Security
- Webhook state changes require provider-adapter signature verification over the raw payload.
- Public status and administration projections omit secrets, claim hashes, provider payload references, and unnecessary buyer PII.
- Commerce recovery requires tenant-scoped administrative access, reasons, audit integration, and correction idempotency.

### Documentation
- Added the commerce integration guide with API DTOs, sequence diagrams, lifecycle policies, provider migration, and security guidance.
- Added a Qurvia API and frontend implementation prompt for the complete purchase, onboarding, licensing, seat, administration, and provider migration experience.

### Validation
- Billing tests: 88 passed.
- Licensing tests: 54 passed.
- Full solution build completed with zero errors.

## 2.9.0 - 2026-07-21

### Added
- Added tenant invite list filtering for status, active-only pending invites, and audit-friendly include/exclude switches for expired, redeemed, and revoked invite records.
- Added a combined tenant user directory endpoint that can return active tenant users, pending invite rows, disabled users, or pending-only invite views for admin user-management screens.
- Added tenant admin user provisioning APIs and services to create or link identity users, attach tenant memberships, grant roles, apply access grants, invoke user extension hooks, and optionally send setup invites.
- Added password setup invite support so provisioned email/password users can set their own password through invite acceptance.

### Changed
- Tenant invite listing now supports effective expiration filtering without purging invite records.
- User extension context now includes the `admin-provisioned` operation for consuming apps that create or update app-owned profile rows during admin provisioning.
- Identity API documentation now describes invite filtering, user directory, admin provisioning, password setup, and the IBeam/app profile data boundary.

### Tests
- Added service and API coverage for invite filtering, user directory composition, admin provisioning, and password setup invite acceptance.

## 2.6.0 - 2026-07-20

### Breaking
- AccessControl is now the canonical path for resource grants, permission-role maps, and service-operation permission rules. Identity-owned access-control implementations are considered legacy and should be re-engineered to consume `IBeam.AccessControl` instead of maintaining parallel stores and models.
- Legacy Identity access-control compatibility is intentionally deferred to a later version because current IBeam consumers are internal.
- `IBeam.Identity.Repositories.AzureTable` no longer owns or creates `AccessGrants` or `PermissionRoleMaps`; those tables are owned by `IBeam.AccessControl.Repositories.AzureTable`.
- API credential resource grants now use the canonical AccessControl subject type `api-credential`.

### Added
- Added Azure Table stores in `IBeam.AccessControl.Repositories.AzureTable` for:
  - `IResourceAccessStore`
  - `IPermissionRoleMapStore`
  - `IServiceOperationPermissionStore`
- Added standalone AccessControl API endpoints for permission-role map management under `/api/tenants/{tenantId}/access-control/permission-maps`.

## 2.0.68 - 2026-06-22

### Fixed
- Fixed API credential management authorization to recognize tenant and role claim aliases produced by JWT validation, including `tenant_id`, Microsoft tenant-id URI claims, `roles`, `ClaimTypes.Role`, and JSON-array role values.

### Tests
- Added API credential controller authorization tests for mapped tenant claims, `Administrator`, `Owner`, and array-shaped role claims.

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
