# IBeam Identity Migration Readout

This readout helps teams inspect Azure Table Storage identity data before any cleanup or migration work.
It is intentionally read-only.

Script:

```powershell
.\scripts\identity\read-identity-schema-drift.ps1
```

## Usage

Pass a storage connection string directly:

```powershell
.\scripts\identity\read-identity-schema-drift.ps1 `
  -ConnectionString "<storage-connection-string>" `
  -TablePrefix IBeam
```

Or use an environment variable:

```powershell
$env:IBEAM_IDENTITY_AZURE_TABLE_CONNECTION_STRING = "<storage-connection-string>"
.\scripts\identity\read-identity-schema-drift.ps1
```

For Azurite/local storage, pass the expanded connection string with `AccountName`, `AccountKey`,
and `TableEndpoint`; the `UseDevelopmentStorage=true` shorthand is not supported by this script.

Write a JSON report:

```powershell
.\scripts\identity\read-identity-schema-drift.ps1 `
  -ConnectionString "<storage-connection-string>" `
  -OutputPath .\artifacts\identity-readout.json
```

Print JSON to stdout:

```powershell
.\scripts\identity\read-identity-schema-drift.ps1 `
  -ConnectionString "<storage-connection-string>" `
  -AsJson
```

## What It Checks

- Deprecated dynamic columns still present on stored rows:
  `PermissionsJson`, `CodeNonce`, `ResendAvailableAt`, `UserDisplayName` on `IBeamUserTenants`,
  `OwnerUserId`, and `DestinationHash`.
- Tenant user projection gaps:
  missing `UserDisplayName` or `Email` on `IBeamTenantUsers`.
- Role migration health:
  membership rows with `RolesCsv` but missing `RoleIdsCsv`.
- Legacy role-name grants:
  `RoleNamesCsv` usage on permission maps and API credentials.
- Unknown columns:
  columns in storage that are not part of the expected first-party schema list used by the readout.

## Defaults

- `TablePrefix`: `IBeam`
- `UserTableName`: `IdentityUsers`
- `TenantsTableName`: `Tenants`
- `TenantUsersTableName`: `TenantUsers`
- `UserTenantsTableName`: `UserTenants`
- `OtpChallengesTableName`: `OtpChallenges`
- `AuthAttemptsTableName`: `AuthAttempts`

The script reads all matching rows from the configured tables and reports counts plus samples.
It does not update, delete, migrate, or backfill data.
