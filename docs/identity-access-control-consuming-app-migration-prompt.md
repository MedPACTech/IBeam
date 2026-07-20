# Consuming App Migration Prompt: IBeam Identity AccessControl Rework

You are updating a consuming application after a breaking IBeam change that moved access-control persistence and services out of `IBeam.Identity` and into `IBeam.AccessControl`.

The consuming app is internal-only, so do not add legacy compatibility shims. Prefer direct breaking-change fixes.

## Goal

Update the app so it consumes the canonical IBeam AccessControl path for resource grants, permission-role maps, and service-operation permission rules while continuing to use IBeam Identity for authentication, users, tenants, API credentials, and Identity-specific access composition.

## What Changed

AccessControl is now the canonical package boundary for:

- Resource access grants.
- Permission-to-role maps.
- Service-operation permission rules.
- Standalone BYO-auth access-control endpoints for apps that do not want IBeam Identity.

Identity now depends on AccessControl instead of owning parallel access-control stores.

Identity still owns:

- Users and tenants.
- API credentials.
- Authentication and principal construction.
- Identity API controllers.
- Identity-specific access composition, including `IIBeamAccessControlService` and current-principal access helpers.

## Breaking Changes

Remove any consuming-app dependency on these old Identity-owned types:

- `IIBeamAccessGrantStore`
- `IPermissionAccessStore`
- `AzureTableAccessGrantStore`
- `AzureTablePermissionAccessStore`
- Identity-owned `AccessGrantEntity`
- Identity-owned `PermissionRoleMapEntity`
- `AzureTableIdentityOptions.AccessGrantsTableName`
- `AzureTableIdentityOptions.PermissionRoleMapsTableName`

Replace them with AccessControl contracts/services:

- `IBeam.AccessControl.IResourceAccessStore`
- `IBeam.AccessControl.IPermissionRoleMapStore`
- `IBeam.AccessControl.Services.IResourceAccessService`
- `IBeam.AccessControl.Services.IPermissionRoleMapService`
- `IBeam.AccessControl.Services.IResourceAccessAuthorizer`
- `IBeam.AccessControl.Repositories.AzureTable.AddIBeamAccessControlAzureTableStores(...)`

API credential resource grants now use this canonical subject type:

```json
"api-credential"
```

Do not keep or introduce the old resource-grant subject type:

```json
"apiCredential"
```

It is still okay for code symbols, route names, or operation names to contain `ApiCredential` or `apiCredentials.rotate`. Only resource-grant subject type values and principal type claim values must use `api-credential`.

## Package References

Inspect the consuming app's project files and add missing references as needed.

If the app uses Identity services, make sure the relevant projects can resolve:

- `IBeam.Identity`
- `IBeam.Identity.Services`
- `IBeam.Identity.Api`
- `IBeam.AccessControl`
- `IBeam.AccessControl.Services`

If the app uses Azure Table persistence for Identity/AccessControl, make sure the relevant project can resolve:

- `IBeam.Identity.Repositories.AzureTable`
- `IBeam.AccessControl.Repositories.AzureTable`

## Dependency Injection Updates

For apps using Identity with Azure Table persistence, the normal Identity Azure Table registration should now register AccessControl Azure stores too:

```csharp
builder.Services.AddIBeamIdentityAzureTable(builder.Configuration);
```

If the app registers AccessControl stores directly, use:

```csharp
builder.Services.AddIBeamAccessControlAzureTableStores(builder.Configuration);
```

If the app registers Identity services directly, it should rely on Identity's updated service registration for the canonical in-memory AccessControl service set. Avoid re-registering old no-op Identity access stores.

Remove any app-level registrations similar to:

```csharp
services.AddSingleton<IIBeamAccessGrantStore, ...>();
services.AddSingleton<IPermissionAccessStore, ...>();
services.AddScoped<IIBeamAccessGrantStore, ...>();
services.AddScoped<IPermissionAccessStore, ...>();
```

Replace with AccessControl registrations only if the app provides custom implementations:

```csharp
services.AddSingleton<IResourceAccessStore, CustomResourceAccessStore>();
services.AddSingleton<IPermissionRoleMapStore, CustomPermissionRoleMapStore>();
```

Use scoped lifetimes if the custom stores depend on scoped services.

## Configuration Updates

Identity Azure Table options no longer own these table settings:

- `AccessGrantsTableName`
- `PermissionRoleMapsTableName`

Remove those settings from Identity config sections.

AccessControl Azure Table options now own the access-control table settings. Configure them under the app's AccessControl Azure Table section if the app customizes table names:

```json
{
  "AccessControl": {
    "AzureTable": {
      "ConnectionString": "...",
      "TablePrefix": "IBeam",
      "ResourceAccessGrantsTableName": "AccessGrants",
      "PermissionRoleMapsTableName": "PermissionRoleMaps",
      "ServiceOperationPermissionsTableName": "ServiceOperationPermissions"
    }
  }
}
```

If the app relies on default table names, no table-name config is required.

## Schema Ownership Updates

Identity schema management no longer creates or validates:

- `AccessGrants`
- `PermissionRoleMaps`
- `ServiceOperationPermissions`

Those tables are owned by `IBeam.AccessControl.Repositories.AzureTable`.

Update migrations, schema checks, drift scripts, seed scripts, deployment docs, and infrastructure manifests accordingly.

Expected default full table names with the default `IBeam` prefix:

- `IBeamAccessGrants`
- `IBeamPermissionRoleMaps`
- `IBeamServiceOperationPermissions`

Do not delete existing production/internal data unless explicitly instructed. This migration is a code ownership change first; existing tables may remain the same physical Azure Tables if names match the new AccessControl defaults.

## API Usage Updates

For Identity API resource grants, update payloads that target API credentials.

Before:

```json
{
  "subjectType": "apiCredential",
  "subjectId": "credential-id"
}
```

After:

```json
{
  "subjectType": "api-credential",
  "subjectId": "credential-id"
}
```

Also update any tests or clients that assert:

```text
principal_type = apiCredential
```

Expected value:

```text
principal_type = api-credential
```

Keep this claim unchanged when present:

```text
api_subject_type = credential
```

## Endpoint Notes

`IBeam.AccessControl.Api` can be used by apps that want developer-facing AccessControl endpoints without using IBeam Identity.

Standalone AccessControl endpoints include permission-role map management under:

```text
/api/tenants/{tenantId}/access-control/permission-maps
```

Identity API still exposes Identity-aware access-control endpoints for apps that use IBeam Identity.

Use Identity API endpoints when the app needs Identity current-user/current-credential behavior. Use standalone AccessControl API endpoints when the app provides its own auth and only wants AccessControl management/evaluation.

## Search And Replace Checklist

Search the consuming app for:

```text
IIBeamAccessGrantStore
IPermissionAccessStore
AzureTableAccessGrantStore
AzureTablePermissionAccessStore
AccessGrantsTableName
PermissionRoleMapsTableName
apiCredential
```

Handle results as follows:

- Remove or replace old Identity store/interface usage with AccessControl contracts.
- Move Identity table config for grants/maps into AccessControl config.
- Change JSON payload values and claim assertions from `apiCredential` to `api-credential`.
- Leave C# type names, variable names, API credential route names, and operation names alone unless they are actual persisted subject-type or principal-type values.

## Tests To Update

Update tests that mock or fake old Identity-owned access stores.

Old fake targets:

```csharp
IIBeamAccessGrantStore
IPermissionAccessStore
```

New fake targets:

```csharp
IResourceAccessStore
IPermissionRoleMapStore
IResourceAccessService
IPermissionRoleMapService
```

Prefer faking services in API/controller tests and stores in service-level tests.

Update expected grant records to use AccessControl models such as:

- `ResourceAccessGrantRecord`
- `PermissionRoleMapRecord`
- `PermissionGrantSet`

## Validation Commands

Run the consuming app's normal restore/build/test flow. At minimum, run the project or solution build and all Identity/AccessControl-related tests.

Example:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-restore
```

If the app has targeted test projects, also run those directly.

## Acceptance Criteria

The migration is complete when:

- The app builds without references to removed Identity access-control stores/interfaces.
- Identity Azure Table config no longer contains `AccessGrantsTableName` or `PermissionRoleMapsTableName`.
- AccessControl Azure Table config owns resource grants, permission-role maps, and service-operation permission tables.
- API credential grants and principal type assertions use `api-credential`.
- Existing Identity flows still work: login/auth, tenant selection, current-user access context, and API credential auth.
- Access-control management/evaluation works through the appropriate Identity API or standalone AccessControl API path.
- Tests pass without legacy shims.

## Suggested Implementation Order

1. Update package/project references.
2. Fix DI registrations.
3. Move table configuration from Identity to AccessControl.
4. Replace old Identity store/interface usage with AccessControl contracts or services.
5. Update API clients and tests from `apiCredential` to `api-credential` where it is a subject/principal type value.
6. Update schema/drift/deployment scripts so AccessControl owns the access-control tables.
7. Run build and tests.

