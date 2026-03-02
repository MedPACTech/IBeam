# IBeam.Identity.Storage.AzureTable

This folder currently does not contain active source code.

## Current status

- Only `bin/` and `obj/` artifacts are present.
- The active Azure Table implementation has moved to:
  - `IBeam.Identity.Repositories.AzureTable`

## Use this instead

For Azure Table identity persistence, use:

- Project: `IBeam.Identity.Repositories.AzureTable`
- Registration: `AddIBeamIdentityAzureTable(configuration)`
- Config section: `IBeam:Identity:AzureTable`

### Key settings

- `StorageConnectionString`
- `TablePrefix`
- `IndexTableName`
- `UserTableName`
- `RoleTableName`
- `TenantsTableName`
- `TenantUsersTableName`
- `UserTenantsTableName`
- `OtpChallengesTableName`
- `ExternalLoginsTableName`
- `AuthSessionsTableName`

If this folder is intended to be revived, add a `.csproj` and source files, then update this README.
