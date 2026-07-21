# IBeam Azure Table Identity Schema Inventory

This document inventories the Azure Table Storage schema used by `IBeam.Identity.Repositories.AzureTable`.
It assumes the deployment uses `IBeam` as `IBeam:Identity:AzureTable:TablePrefix`, so physical table names
are shown as `IBeam{BaseTableName}`.

Note: the package code defaults the ElCamino/ASP.NET Identity table base names to `AspNetUsers`,
`AspNetRoles`, and `AspNetIndex`, but our environment uses the configured base names
`IdentityUsers`, `IdentityRoles`, and `IdentityIndex`. The inventory below uses the configured
physical names.

Source areas:

- `IBeam.Identity.Repositories.AzureTable/Options/AzureTableIdentityOptions.cs`
- `IBeam.Identity.Repositories.AzureTable/Entities/*.cs`
- `IBeam.Identity.Repositories.AzureTable/Stores/*.cs`
- `IBeam.Identity.Repositories.AzureTable/Schema/AzureTableIdentitySchemaManager.cs`

Each custom table also has Azure Table infrastructure fields:

| Field | Description | Logical usage |
|---|---|---|
| `PartitionKey` | Azure Table partition key. | Primary query boundary. |
| `RowKey` | Azure Table row key. | Point lookup or item identity within a partition. |
| `Timestamp` | Azure Table service timestamp. | Service-managed concurrency/audit metadata. |
| `ETag` | Azure Table entity version. | Used for optimistic concurrency on updates. |

## Naming

| Option | Code default base table | Our configured base table | Physical table with `IBeam` prefix |
|---|---:|---:|---:|
| `UserTableName` | `AspNetUsers` | `IdentityUsers` | `IBeamIdentityUsers` |
| `RoleTableName` | `AspNetRoles` | `IdentityRoles` | `IBeamIdentityRoles` |
| `IndexTableName` | `AspNetIndex` | `IdentityIndex` | `IBeamIdentityIndex` |
| `TenantsTableName` | `Tenants` | `Tenants` | `IBeamTenants` |
| `TenantUsersTableName` | `TenantUsers` | `TenantUsers` | `IBeamTenantUsers` |
| `UserTenantsTableName` | `UserTenants` | `UserTenants` | `IBeamUserTenants` |
| `TenantRolesTableName` | `Roles` | `Roles` | `IBeamRoles` |
| `TenantInvitesTableName` | `TenantInvites` | `TenantInvites` | `IBeamTenantInvites` |
| `OtpChallengesTableName` | `OtpChallenges` | `OtpChallenges` | `IBeamOtpChallenges` |
| `AuthIdentifiersTableName` | `AuthIdentifiers` | `AuthIdentifiers` | `IBeamAuthIdentifiers` |
| `ExternalLoginsTableName` | `ExternalLogins` | `ExternalLogins` | `IBeamExternalLogins` |
| `AuthSessionsTableName` | `AuthSessions` | `AuthSessions` | `IBeamAuthSessions` |
| `ApiCredentialsTableName` | `ApiCredentials` | `ApiCredentials` | `IBeamApiCredentials` |
| `AccessCatalogOverridesTableName` | `AccessCatalogOverrides` | `AccessCatalogOverrides` | `IBeamAccessCatalogOverrides` |
| `AuthAttemptsTableName` | `AuthAttempts` | `AuthAttempts` | `IBeamAuthAttempts` |
| `SystemLogsTableName` | `SystemLogs` | `SystemLogs` | `IBeamSystemLogs` |
| `SystemErrorsTableName` | `SystemErrors` | `SystemErrors` | `IBeamSystemErrors` |
| Schema table | `Schema` | `Schema` | `IBeamSchema` |

OTP verification failures, OTP start throttles, and IP throttles now use `IBeamAuthAttempts` with distinct
`Method` values instead of a separate table.

`IBeamSystemLogs` is now owned by `IBeam.Services.Logging`. `SystemLogsTableName` remains on
`AzureTableIdentityOptions` only as a compatibility/pass-through setting when
`AddIBeamIdentityAzureTable` registers the logging package sink.

## OTP attempt controls

These settings live under `IBeam:Identity:Otp` and control rows written to `IBeamAuthAttempts`.

| Setting | Default | Logical usage |
|---|---:|---|
| `MaxAttempts` | `5` | Failed OTP verifications allowed per destination before locking `Method = otp`. Set `0` to disable destination verification lockout. |
| `LockoutMinutes` | `10` | Duration of the destination OTP verification lockout. |
| `MaxChallengeRequests` | `5` | OTP challenge starts allowed per destination and per IP before locking `Method = otp-start` / `otp-start-ip`. Set `0` to disable challenge request throttling. |
| `ChallengeRequestLockoutMinutes` | `15` | Duration of OTP challenge request lockout. |
| `MaxFailedAttemptsPerIp` | `20` | Failed OTP verifications allowed per IP before locking `Method = otp-ip`. Set `0` to disable IP-based OTP failure lockout. |
| `IpLockoutMinutes` | `30` | Duration of IP-based OTP verification lockout. |
| `FailureResponseDelayMilliseconds` | `250` | Adds a small delay before failed/locked OTP responses to make brute-force loops more expensive. Set `0` to disable. |
| `TrackAttemptMetadata` | `true` | Stores IP/device/location/correlation metadata on auth-attempt rows when available. |

## IBeamIdentityUsers

ElCamino/ASP.NET Identity owns this table. IBeam stores canonical users here through `ApplicationUser`,
which extends ElCamino `IdentityUser` with `PreferredTwoFactorMethod`.

| Field | Description | Logical usage |
|---|---|---|
| `PartitionKey` | Provider-generated user partition key. | ElCamino point/query lookups. |
| `RowKey` | Provider-generated user row key. | ElCamino point lookup. |
| `Timestamp` | Azure Table service timestamp. | Provider-managed metadata. |
| `ETag` | Azure Table entity version. | Provider-managed optimistic concurrency. |
| `Id` | User id. IBeam expects this to be a GUID string. | Mapped to `IdentityUser.UserId`; used by tenant membership, sessions, credentials, and auth identifiers. |
| `KeyVersion` | ElCamino key format version. | Provider-managed key compatibility metadata. |
| `UserName` | Login username. IBeam sets this to normalized email when email exists, otherwise normalized phone. | Used by ASP.NET Identity/ElCamino lookups. |
| `NormalizedUserName` | Normalized username. | Provider-managed username lookup/index field. |
| `Email` | Email address, normalized by IBeam before storage. | Used by password auth, email OTP, and `FindByEmailAsync`; mirrored into `IBeamAuthIdentifiers`. |
| `NormalizedEmail` | Normalized email lookup value. | Provider-managed email lookup/index field. |
| `EmailConfirmed` | Whether email has been verified. | Set after email verification/password registration flow. |
| `PhoneNumber` | Normalized phone number. | Used by SMS auth and `FindByPhoneAsync`; mirrored into `IBeamAuthIdentifiers`. |
| `PhoneNumberConfirmed` | Whether phone has been verified. | Set after phone verification flow. |
| `PasswordHash` | ASP.NET Identity password hash. | Verified in password login; updated by set/reset password flows. |
| `SecurityStamp` | ASP.NET Identity security stamp. | Updated when password changes. |
| `ConcurrencyStamp` | ASP.NET Identity concurrency token. | Provider-managed optimistic concurrency at identity layer. |
| `TwoFactorEnabled` | Whether 2FA is enabled. | Mapped to IBeam identity user; checked by auth flows. |
| `PreferredTwoFactorMethod` | Preferred IBeam 2FA method, for example email or sms. | IBeam extension field set by `SetTwoFactorAsync`. |
| `LockoutEnd` | ASP.NET Identity lockout end timestamp. | Provider-managed; IBeam currently uses `IBeamAuthAttempts` for auth lockout state. |
| `LockoutEndDateUtc` | ElCamino compatibility lockout timestamp. | Provider-managed compatibility field. |
| `LockoutEnabled` | Whether ASP.NET Identity lockout is enabled for the user. | Provider-managed; not the primary IBeam lockout path. |
| `AccessFailedCount` | ASP.NET Identity failed access count. | Provider-managed; IBeam auth attempt counts live in `IBeamAuthAttempts`. |

## IBeamIdentityRoles

ElCamino/ASP.NET Identity owns this table. IBeam registers role stores but tenant-scoped roles live in `IBeamRoles`.

| Field | Description | Logical usage |
|---|---|---|
| `PartitionKey` | Provider-generated role partition key. | ElCamino point/query lookups. |
| `RowKey` | Provider-generated role row key. | ElCamino point lookup. |
| `Timestamp` | Azure Table service timestamp. | Provider-managed metadata. |
| `ETag` | Azure Table entity version. | Provider-managed optimistic concurrency. |
| `Id` | ASP.NET Identity role id. | Provider role identity, not the tenant role id used by `IBeamRoles`. |
| `KeyVersion` | ElCamino key format version. | Provider-managed key compatibility metadata. |
| `Name` | ASP.NET Identity role name. | Provider-managed. |
| `NormalizedName` | Normalized ASP.NET Identity role name. | Provider-managed lookup. |
| `ConcurrencyStamp` | ASP.NET Identity concurrency token. | Provider-managed. |
| Role claim fields | Claim type/value rows may be stored by the provider. | Provider-managed. Role claims are not the same as tenant roles in `IBeamRoles`. |

## IBeamIdentityIndex

ElCamino owns this table for secondary indexes.

| Field | Description | Logical usage |
|---|---|---|
| `PartitionKey` | Provider-generated index partition. | Email, username, role, login, token, and id lookup indexes. |
| `RowKey` | Provider-generated index row. | Provider point lookup. |
| Provider payload fields | ElCamino index metadata and target ids. | Provider-managed. IBeam code should not depend directly on this schema. |

## IBeamTenants

Purpose: canonical tenant/workspace records.

Keys:

- `PartitionKey = TEN`
- `RowKey = {tenantId:D}`

| Field | Description | Logical usage |
|---|---|---|
| `Name` | Tenant display name. | Returned in tenant APIs and used to enrich memberships. |
| `NormalizedName` | Normalized tenant name. | Set on create/update, exposed through `IdentityTenant`, and retained for name search/uniqueness scenarios. |
| `Status` | Tenant status, usually `Active` or `Disabled`. | Used by tenant lifecycle APIs and returned as active state. |
| `CreatedAt` | Tenant creation timestamp. | Returned in tenant model. |
| `UpdatedAt` | Last tenant update timestamp. | Updated on tenant status/name changes. |

## IBeamTenantUsers

Purpose: tenant-to-user membership lookup. This is the efficient table for listing users in a tenant.

Keys:

- `PartitionKey = TEN|{tenantId:D}`
- `RowKey = USR|{userId:D}`

Legacy comments and parsers also reference `TEN#{tenantId}` / `USR#{userId}`.

| Field | Description | Logical usage |
|---|---|---|
| `TenantId` | Denormalized tenant id. | Used to map `TenantUserInfo`; avoids parsing keys. |
| `UserId` | Denormalized user id. | Used to map `TenantUserInfo`; fallback parsing exists from `RowKey`. |
| `Status` | Membership status, usually `Active` or `Disabled`. | Used to filter active role resolution and active membership state. |
| `CreatedAt` | Membership creation timestamp. | Returned in tenant-user APIs. |
| `DisabledAt` | When membership was disabled. | Set by disable membership flow and returned in API model. |
| `DisabledReason` | Optional disable reason. | Set by disable membership flow and returned in API model. |
| `UserDisplayName` | Optional denormalized user display name. | Populated when user details are available during tenant-user linking/bootstrap and returned in `TenantUserInfo`. |
| `Email` | Optional denormalized user email. | Populated when user details are available during tenant-user linking/bootstrap and returned in `TenantUserInfo`. |
| `RolesCsv` | Comma-separated role names assigned to the user in this tenant. | Still active: returned as role names, updated on grants/revokes/renames, and used as fallback when `RoleIdsCsv` is absent. Legacy but not removable without migration. |
| `RoleIdsCsv` | Comma-separated tenant role ids assigned to the user. | Canonical role assignment field used by `GetRolesForUserAsync`, grant/revoke, and role rename/delete synchronization. |

## IBeamUserTenants

Purpose: user-to-tenant reverse membership lookup. This is the efficient table for listing tenants available to a user.

Keys:

- `PartitionKey = USR|{userId:D}`
- `RowKey = TEN|{tenantId:D}`

Legacy comments and parsers also reference `USR#{userId}` / `TEN#{tenantId}`.

| Field | Description | Logical usage |
|---|---|---|
| `UserId` | Denormalized user id. | Used by role update synchronization and membership mapping. |
| `TenantId` | Denormalized tenant id. | Used to map `TenantInfo`; fallback parsing exists from `RowKey`. |
| `Status` | Membership status. | Used to determine tenant active state for the user. |
| `CreatedAt` | Membership creation timestamp. | Stored for audit/history. Not exposed by `TenantInfo`. |
| `DisabledAt` | When membership was disabled. | Set by disable membership flow. Not exposed by `TenantInfo`. |
| `DisabledReason` | Optional disable reason. | Set by disable membership flow. Not exposed by `TenantInfo`. |
| `TenantDisplayName` | Optional denormalized tenant display name. | Used as fallback when `IBeamTenants` does not have a name. |
| `RolesCsv` | Comma-separated tenant role names for this user. | Returned in `TenantInfo.Roles` and kept in sync with `IBeamTenantUsers`. Legacy/display compatibility field. |
| `RoleIdsCsv` | Comma-separated tenant role ids. | Returned in `TenantInfo.RoleIds` and kept in sync with `IBeamTenantUsers`. |
| `IsDefault` | Whether this tenant is the user's default tenant. | Queried by `GetDefaultTenantIdAsync`; updated by `SetDefaultTenantAsync`. |
| `LastSelectedAt` | Last time tenant was selected/defaulted. | Updated when setting default tenant. |

## IBeamRoles

Purpose: tenant-scoped role catalog. This is the role table used by IBeam authorization and membership grants.

Keys:

- `PartitionKey = TEN|{tenantId:D}`
- `RowKey = ROL|{roleId:D}`

| Field | Description | Logical usage |
|---|---|---|
| `TenantId` | Tenant owning the role. | Used for mapping and validation. |
| `RoleId` | Role id. | Used for point lookup, grant/revoke, and membership `RoleIdsCsv`. |
| `Name` | Display/semantic role name. | Returned by role APIs and mirrored into membership `RolesCsv`. |
| `NormalizedName` | Uppercase normalized role name. | Used to enforce per-tenant role-name uniqueness and get-or-create by name. |
| `IsSystem` | Whether the role is framework/system protected. | Prevents rename/delete for system roles. |
| `Status` | Role status, usually `Active` or `Disabled`. | Used to filter role queries and role assignment resolution. |
| `CreatedAt` | Role creation timestamp. | Returned in role model. |
| `UpdatedAt` | Last role update timestamp. | Updated on rename/disable. |

## IBeamPermissionRoleMaps

Moved: this table is now owned by `IBeam.AccessControl.Repositories.AzureTable`, not `IBeam.Identity.Repositories.AzureTable`.
It remains listed here only for historical schema inventory.

Purpose: tenant permission-to-role mapping. This answers "which roles grant this permission?"

Keys:

- `PartitionKey = TEN|{tenantId:D}`
- `RowKey = NAM|{sha256(permissionName)}` for name-based mappings
- `RowKey = ID|{permissionId:D}` for id-based mappings

| Field | Description | Logical usage |
|---|---|---|
| `TenantId` | Tenant owning the mapping. | Used for mapping and validation. |
| `PermissionName` | Permission name when mapping by name. | Used to return mapping metadata. Lookup uses hashed row key, not this field. |
| `PermissionId` | Permission id when mapping by id. | Used to return mapping metadata. Lookup uses `ID|{permissionId}` row key. |
| `RoleNamesCsv` | Role names allowed for the permission. | Used by permission authorizer grant resolution. |
| `RoleIdsCsv` | Role ids allowed for the permission. | Used by permission authorizer grant resolution. |
| `Status` | Mapping status. | `ResolveGrantsAsync` ignores non-active mappings. |
| `UpdatedAt` | Last mapping update timestamp. | Returned in mapping model. |

## IBeamAccessGrants

Moved: this table is now owned by `IBeam.AccessControl.Repositories.AzureTable`, not `IBeam.Identity.Repositories.AzureTable`.
It remains listed here only for historical schema inventory.

Purpose: tenant resource access grants for subject/resource/access-level checks.

Keys:

- `PartitionKey = TEN|{tenantId:D}`
- `RowKey = GRA|{grantId:D}`

| Field | Description | Logical usage |
|---|---|---|
| `TenantId` | Tenant owning the grant. | Used for mapping and validation. |
| `GrantId` | Grant id. | Used for point lookup and soft delete. |
| `SubjectType` | Subject category, for example user, role, credential, or agent. | Used to filter `GetGrantsAsync`. |
| `SubjectId` | Subject identifier. | Used to filter `GetGrantsAsync`. |
| `ResourceType` | Resource category. | Returned to access-control services. |
| `ResourceId` | Resource identifier. | Returned to access-control services. |
| `AccessLevel` | Granted access level. | Used by access-control authorization. |
| `Status` | Grant status. | Active grants are returned; delete sets `Disabled`. |
| `CreatedAt` | Grant creation timestamp. | Returned in grant model. |
| `UpdatedAt` | Last grant update timestamp. | Set on upsert/delete. |

## IBeamAccessCatalogOverrides

Purpose: tenant-specific overrides/additions to the access catalog.

Keys:

- `PartitionKey = TEN|{tenantId:D}`
- `RowKey = CAT|{catalogItemId:D}`

| Field | Description | Logical usage |
|---|---|---|
| `TenantId` | Tenant owning the override. | Used for mapping and validation. |
| `CatalogItemId` | Override/catalog item id. | Used for point lookup and soft delete. |
| `Key` | Stable catalog key. | Returned to catalog consumers; sorted after category. |
| `Label` | Display label. | Returned to catalog consumers. |
| `Description` | Optional description. | Returned to catalog consumers. |
| `Category` | Catalog grouping/category. | Used for sorting results. |
| `IsAssignable` | Whether grants can be assigned for this item. | Returned to access-control UI/service consumers. |
| `IsMutable` | Whether tenant can mutate this item. | Returned to access-control UI/service consumers. |
| `IsEnabled` | Whether item is enabled. | Returned to access-control UI/service consumers. |
| `SubjectTypesCsv` | Allowed subject types. | Parsed and returned as list. |
| `ResourceType` | Optional resource type target. | Returned to access-control consumers. |
| `ResourceId` | Optional resource id target. | Returned to access-control consumers. |
| `ParentResourceType` | Optional parent resource type. | Returned for hierarchy/context. |
| `ParentResourceId` | Optional parent resource id. | Returned for hierarchy/context. |
| `SupportedAccessLevelsCsv` | Supported access levels. | Parsed and returned as list. |
| `Rank` | Optional sort/display rank. | Returned to consumers. |
| `ModuleKey` | Optional module/feature grouping. | Returned to consumers. |
| `RequiredAccessLevel` | Optional access level required to manage/use item. | Returned to consumers. |
| `IsDangerous` | Whether the item represents a sensitive operation. | Returned to consumers. |
| `IdParameter` | Optional route/template id parameter name. | Returned to consumers. |
| `Status` | Override status. | Active overrides are returned; delete sets `Disabled`. |
| `CreatedAt` | Creation timestamp. | Returned in override model. |
| `UpdatedAt` | Last update timestamp. | Set on upsert/delete. |

## IBeamTenantInvites

Purpose: tenant invitation records and token lookup rows for tenant-managed onboarding.

Keys:

- Tenant list/point lookup row: `PartitionKey = TEN|{tenantId:D}`, `RowKey = INV|{inviteId:D}`
- Token lookup row: `PartitionKey = TOK|{sha256(inviteToken)}`, `RowKey = INV`

The store writes two rows per active invite: one tenant-scoped row for tenant admin list/get operations and one token-index row for preview/accept. Invite tokens are never stored in plaintext; only `TokenHash` is persisted. When an invite is resent, the previous token-index row is removed and a new token hash is written.

| Field | Description | Logical usage |
|---|---|---|
| `InviteId` | Invite id. | Used for tenant admin point lookup, resend, revoke, and API responses. |
| `TenantId` | Tenant owning the invite. | Used for tenant context during preview/accept and role/access assignment. |
| `DestinationType` | Invite channel: `email` or `sms`. | Determines destination validation and delivery channel. |
| `NormalizedDestination` | Normalized email or phone number. | Used to verify the accepting user controls the invited destination. |
| `TokenHash` | SHA-256 hash of the invite token. | Used for token lookup without storing plaintext tokens. |
| `Status` | Invite state: `pending`, `sent`, `redeemed`, `revoked`, or `expired`. | Used to block reuse, revoked invites, and expired invites. |
| `CreatedUtc` | Creation timestamp. | Returned in invite APIs and used for sorting. |
| `InvitedByUserId` | User who created the invite. | Audit metadata and created-by context for optional access grants. |
| `ExpiresUtc` | Invite expiration timestamp. | Checked before preview/resend/accept; expired invites are marked `expired`. |
| `SentUtc` | Latest send timestamp. | Updated when the invite is sent or resent. |
| `RedeemedUtc` | Redemption timestamp. | Set after successful acceptance. |
| `RedeemedByUserId` | User that accepted the invite. | Audit metadata and idempotency context. |
| `RevokedUtc` | Revocation timestamp. | Set when an admin revokes the invite. |
| `RevokedByUserId` | User who revoked the invite. | Audit metadata. |
| `RevokedReason` | Optional revoke reason. | Returned for admin review. |
| `DisplayName` | Initial display-name hint. | Passed to `IIdentityUserExtensionCoordinator` during acceptance. |
| `FirstName` | Initial first-name hint. | Passed to the host-owned user extension row. |
| `LastName` | Initial last-name hint. | Passed to the host-owned user extension row. |
| `ProfileMetadataJson` | App metadata for profile initialization. | Passed through extension context; IBeam does not interpret app-specific fields. |
| `RoleIdsCsv` | Comma-separated tenant role ids requested for acceptance. | Applied through `ITenantRoleService`. |
| `RoleNamesJson` | Role names requested for acceptance. | Ensured/granted through `ITenantRoleService`. |
| `SetAsDefaultTenant` | Whether acceptance should make this tenant the user's default. | Applied during tenant membership bootstrap. |
| `AccessGrantsJson` | Optional resource grants to apply after acceptance. | Applied through `IResourceAccessService` when registered. |
| `RedirectUrl` | Host-provided invite landing/acceptance URL. | Used by `ITenantInviteUrlBuilder`. |
| `CorrelationId` | Correlation id for lifecycle events. | Links create/send/accept/revoke events. |
| `CausationId` | Causation id for lifecycle events. | Links follow-on events to initiating actions. |
| `MetadataJson` | Invite-level metadata. | Returned to services and passed to extension/delivery hooks. |

## IBeamAuthIdentifiers

Purpose: fast auth identifier binding from email/SMS to user id.

Keys:

- `PartitionKey = AUTH|EMAIL|{normalizedEmailUpper}`
- `PartitionKey = AUTH|SMS|{normalizedPhoneUpper}`
- `RowKey = USER`

| Field | Description | Logical usage |
|---|---|---|
| `UserId` | Bound canonical user id. | Used by email/SMS login lookup to load `IBeamIdentityUsers`. |
| `IdentifierType` | Identifier type, usually `email` or `sms`. | Stored for diagnostics/model clarity. Lookup uses key. |
| `Identifier` | Normalized identifier value. | Stored for diagnostics/model clarity. Lookup uses key. |
| `BoundAtUtc` | Time identifier was bound. | Audit metadata. |

## IBeamOtpChallenges

Purpose: OTP challenge state for email/SMS login, verification, MFA, and similar flows.

Keys:

- `PartitionKey = OTP-{firstTwoCharactersOfChallengeId}`
- `RowKey = {challengeId}`

There is an older helper in `TableKeys.cs` that describes `otp:{tenant}:{destinationHash}` keys, but the current store uses the `OTP-xx` partition.

| Field | Description | Logical usage |
|---|---|---|
| `ChallengeId` | Challenge id. | Duplicates row key; returned in model. |
| `TenantId` | Optional tenant scope. | Returned in challenge record; null for pre-tenant flows. |
| `Purpose` | Challenge purpose, for example login, MFA, email verification. | Parsed into `SenderPurpose` and checked by auth flows. |
| `Channel` | Delivery channel, email or SMS. | Parsed into `SenderChannel`; fallback infers from destination. |
| `Destination` | Normalized email or phone destination. | Used to verify completion request matches challenge destination. |
| `CodeHash` | Hashed OTP or verification secret. | Used by OTP verification. |
| `CreatedAt` | Challenge creation timestamp. | Written by provider; not currently supplied by abstraction. |
| `ExpiresAt` | Challenge expiry. | Used by OTP verification. |
| `IsConsumed` | Whether challenge was consumed. | Used by OTP verification and mark-consumed flow. |
| `ConsumedAt` | Consumption timestamp. | Set when consumed and retained as audit/replay metadata. |
| `AttemptCount` | Failed/total attempts. | Incremented by OTP attempt tracking. |
| `LastAttemptAt` | Last attempt timestamp. | Set by increment attempt. |
| `VerificationToken` | Token emitted after OTP challenge is consumed. | Used by email/password registration/link completion. |
| `VerificationTokenExpiresAt` | Verification token expiry. | Used by completion flows. |

## IBeamExternalLogins

Purpose: OAuth/external provider account links.

Keys:

- `PartitionKey = PROV|{providerLower}`
- `RowKey = PID|{providerUserIdLower}`

| Field | Description | Logical usage |
|---|---|---|
| `UserId` | IBeam user id linked to external login. | Used to prevent duplicate provider links and map model. |
| `Provider` | Normalized provider key. | Used in partition key and returned in model. |
| `ProviderUserId` | Normalized provider-side user id. | Used in row key and returned in model. |
| `Email` | Optional email from provider. | Returned in external login info. |
| `LinkedAt` | Link creation timestamp. | Audit metadata. |

## IBeamAuthSessions

Purpose: refresh session storage and reverse user-session lookup.

Keys:

- Refresh lookup row: `PartitionKey = RTH|{firstTwoCharsOfRefreshTokenHash}`, `RowKey = {refreshTokenHashLower}`
- User lookup row: `PartitionKey = USR|{userId:D}`, `RowKey = SID|{sessionIdLower}`

The store writes two rows per session: one by refresh token hash and one by user/session id.

| Field | Description | Logical usage |
|---|---|---|
| `SessionId` | Session id. | Used for user-session row key and revoke-by-session. |
| `RefreshTokenHash` | Hashed refresh token. | Used for refresh-token lookup and delete. |
| `UserId` | User id owning session. | Used for user-session partition and model mapping. |
| `TenantId` | Tenant selected for session. | Used in token refresh/session model. |
| `ClaimsJson` | Serialized claims for token refresh. | Used to rebuild refreshed access token claims. |
| `CreatedAt` | Session creation timestamp. | Returned in session record. |
| `LastSeenAt` | Last observed session activity. | Returned in session record. |
| `RefreshTokenExpiresAt` | Refresh token expiry. | Used by token refresh validation. |
| `RevokedAt` | Revocation timestamp. | Used to invalidate/revoke sessions. |
| `DeviceInfo` | Optional device/client metadata. | Returned in session record. |

## IBeamApiCredentials

Purpose: tenant API credentials with hashed secrets and role/agent assignments.

Keys:

- `PartitionKey = TEN|{tenantId:D}`
- `RowKey = CRED|{credentialId:D}`

| Field | Description | Logical usage |
|---|---|---|
| `CredentialId` | Credential id. | Used for point lookup, update, revoke, rotate, and auth. |
| `TenantId` | Tenant owning credential. | Used for tenant list and point lookup. |
| `DisplayName` | Human-readable credential name. | Returned in APIs and used for list sorting. |
| `Description` | Optional credential description. | Returned/updated by credential APIs. |
| `AgentKey` | Optional primary agent identity. | Used to create API principal claims and access context. |
| `AgentDisplayName` | Optional display name for agent. | Returned/updated by credential APIs. |
| `AllowedAgentKeysCsv` | Comma-separated additional agent keys this credential can act as. | Parsed by API credential access service and emitted as claims. |
| `KeyPrefix` | Public key prefix. | Used for credential display and key parsing context. |
| `SecretHash` | Hash of API credential secret. | Verified by API key authenticator; rotated by rotate-secret flow. |
| `RoleNamesCsv` | Comma-separated role names assigned to the credential. | Used to build credential access context and principal claims. |
| `RoleIdsCsv` | Comma-separated role ids assigned to the credential. | Used to build credential access context and principal claims. |
| `CreatedUtc` | Creation timestamp. | Returned and used for list ordering. |
| `CreatedByUserId` | User who created credential. | Returned in credential model. |
| `ExpiresUtc` | Optional expiry. | Used by authenticator/service validation. |
| `LastUsedUtc` | Last successful use timestamp. | Updated by authenticator after successful verification. |
| `LastUsedIp` | Last successful client IP. | Updated by authenticator after successful verification. |
| `RotatedUtc` | Last secret rotation timestamp. | Set by rotate-secret flow. |
| `RevokedUtc` | Revocation timestamp. | Used to determine active/revoked credential state. |
| `RevokedByUserId` | User who revoked credential. | Returned in credential model. |
| `RevocationReason` | Optional revocation reason. | Returned in credential model. |
| `IsDeleted` | Soft delete flag. | List/get operations exclude deleted credentials. |

## IBeamAuthAttempts

Purpose: login failure, throttling, and lockout state by auth method and identifier.

Keys:

- `PartitionKey = MTH|{methodLower}`
- `RowKey = IDN|{identifierLower}`

Current method values include:

- `password`: password login failures by normalized email/username.
- `otp`: OTP verification failures by normalized destination.
- `otp-start`: OTP challenge start throttling by normalized destination.
- `otp-ip`: OTP verification failure throttling by IP address.
- `otp-start-ip`: OTP challenge start throttling by IP address.

| Field | Description | Logical usage |
|---|---|---|
| `Method` | Auth method or throttle bucket, for example `password`, `otp`, `otp-start`, `otp-ip`, or `otp-start-ip`. | Stored normalized; partition key drives lookup and separates password, OTP destination, and OTP IP lockouts. |
| `Identifier` | Auth identifier, for example email, phone, or IP address. | Stored normalized; row key drives point lookup for lockout checks and admin unlock. |
| `FailedAttempts` | Failure count. | Used to enforce lockout threshold. |
| `LockedUntilUtc` | Lockout expiry. | Used to block login attempts until expiry. |
| `LastFailedAtUtc` | Last failure timestamp. | Returned in auth attempt state. |
| `LastSucceededAtUtc` | Last success timestamp. | Set on successful auth and returned in state. |
| `LastFailedIp` | Last IP address associated with a failed attempt. | Captured for operations/security review when metadata tracking is enabled. |
| `LastSucceededIp` | Last IP address associated with a successful attempt. | Captured after successful auth and useful for comparing suspicious activity. |
| `LastUserAgent` | Last user agent associated with the attempt row. | Captured for device/browser review. |
| `LastDeviceId` | Optional client-supplied device id from `X-Device-Id`. | Captured for device tracking when clients provide it. |
| `LastCountry` | Country/region signal, for example `CF-IPCountry` or `X-Country`. | Captured to support infrastructure blocks by region when forwarded by edge/proxy infrastructure. |
| `LastRegion` | Optional region signal from `X-Region`. | Captured to support infrastructure/security review. |
| `LastCity` | Optional city signal from `X-City`. | Captured to support infrastructure/security review. |
| `LastCorrelationId` | Request correlation/trace id. | Links attempt rows to API logs and distributed traces. |
| `LastUnlockedAtUtc` | Last manual/admin unlock timestamp. | Set by auth-attempt unlock API. |
| `UnlockedByUserId` | User id that performed the latest unlock. | Set by auth-attempt unlock API when the caller has a user id claim. |
| `UnlockReason` | Optional admin-provided unlock reason. | Stored for audit context. |
| `MetadataJson` | Additional request metadata, currently path/method/host/referer/trace id when available. | Stored for operational triage; not used for lockout decisions. |

## IBeamSystemLogs

Purpose: operational log sink and service audit sink.

Owner: `IBeam.Services.Logging`, not `IBeam.Identity.Repositories.AzureTable`.

Keys:

- `PartitionKey = TENANT|{tenantId}|DAY|{yyyyMMdd}` when tenant context is available.
- `PartitionKey = SYSTEM|DAY|{yyyyMMdd}` for non-tenant/system logs.
- `RowKey = {HHmmssfffffff}|{guid:N}` for event rows.
- `RowKey = ROLLUP|{hash}` for select-audit rollup rows.

| Field | Description | Logical usage |
|---|---|---|
| `Category` | Log category such as `System`, `EntityChange`, or `SelectRollup`. | Separates API/system logs from service audit records. |
| `Source` | Log source/component. | Stored for diagnostics. |
| `Level` | Log level. Defaults to `Information` when empty. | Stored for diagnostics. |
| `Message` | Log message. | Stored for diagnostics. |
| `Detail` | Optional detail payload. | Stored for diagnostics. |
| `ServiceName` | Service that emitted an audit event. | Used for service audit review. |
| `EntityName` | Logical entity name for audited service writes. | Used for entity-level audit review. |
| `Operation` | Framework audit operation such as `Create`, `Update`, `Archive`, or `Delete`. | Used for filtering audit changes. |
| `Action` | Stable operation name such as `products.create` or `patients.discharge`. | Aligns logs with permission maps and service policies. |
| `EntityId` | Entity id when available. | Used to reconstruct an entity history. |
| `TenantId` | Tenant id when available. | Used in partitioning and tenant-scoped review. |
| `ActorId` | User/system actor when available. | Used for accountability. |
| `TraceId` | Optional trace/correlation id. | Stored for diagnostics. |
| `CorrelationId` | Request correlation id. | Links logs to request traces. |
| `IpAddress` | Caller IP address when available. | Used for operational/security review. |
| `UserAgent` | Caller user agent when available. | Used for operational/security review. |
| `DeviceId` | Optional client-supplied device id. | Used for operational/security review. |
| `BeforeJson` | JSON snapshot before an audited entity change. | Used for rollback/investigation. |
| `AfterJson` | JSON snapshot after an audited entity change. | Used for rollback/investigation. |
| `IsSelectRollup` | Whether the row is a read/query rollup. | Used when select audits are enabled. |
| `QuerySignature` | Hashed query signature for select rollups. | Avoids storing raw query details. |
| `FirstSeenUtc` | First timestamp in a rollup window. | Used for select rollup review. |
| `LastSeenUtc` | Last timestamp in a rollup window. | Used for select rollup review. |
| `Count` | Event count for row/rollup. | Used for select rollup totals. |
| `OccurredAtUtc` | Event time. | Used in key generation and diagnostics. |

## IBeamSystemErrors

Purpose: API/system error sink.

Keys:

- `PartitionKey = ERR|{yyyyMMdd}`
- `RowKey = {HHmmssfff}|{guid:N}`

| Field | Description | Logical usage |
|---|---|---|
| `Source` | Error source/component. | Stored for diagnostics. |
| `Path` | Request path. | Stored for diagnostics. |
| `Method` | HTTP method. | Stored for diagnostics. |
| `Message` | Error message. | Stored for diagnostics. |
| `Exception` | Exception detail/string. | Stored for diagnostics. |
| `TraceId` | Trace/correlation id. | Stored for diagnostics. |
| `OccurredAtUtc` | Error time. | Used in key generation and diagnostics. |

## IBeamSchema

Purpose: schema version marker for the Azure Table identity provider.

Keys:

- `PartitionKey = Schema`
- `RowKey = IBeam.Identity`

| Field | Description | Logical usage |
|---|---|---|
| `Version` | Applied schema version. | Used by `GetStatusAsync` to determine pending schema work. |
| `UpdatedAt` | Version write timestamp. | Audit metadata. |

## Extracted Review Queue

These are the fields/options that deserve review before we adjust the schema. The categories are based on
current first-party code usage in the Azure Table provider and Identity packages.

### Recently removed from first-party entities

These may still exist as old dynamic columns on previously written Azure Table rows, but first-party entity
mapping no longer writes or reads them.

| Field | Former table | Reason removed |
|---|---|---|
| `PermissionsJson` | `IBeamTenantUsers` | No current Azure Table store read/write usage was found. Authorization flows through roles, permission maps, and access grants. |
| `PermissionsJson` | `IBeamUserTenants` | Same as above; no current read/write usage was found. |
| `CodeNonce` | `IBeamOtpChallenges` | Current mapper wrote an empty string and never read it back. |
| `ResendAvailableAt` | `IBeamOtpChallenges` | No current store read/write usage was found. |
| `UserDisplayName` | `IBeamUserTenants` | No current read path exists from this table; tenant-user display projection belongs on `IBeamTenantUsers`. |
| `OwnerUserId` | `IBeamTenants` | Roles are the authority for ownership/admin access; a scalar owner field can drift from role assignments. |
| `DestinationHash` | `IBeamOtpChallenges` | OTP lookup is by challenge id and brute-force/rate-limit controls use `IBeamAuthAttempts`. |

### Looks legacy, but still active

| Field | Table | Current usage | Cleanup path |
|---|---|---|---|
| `RolesCsv` | `IBeamTenantUsers` | Returned as role names, updated on grants/revokes/renames, and used as fallback when `RoleIdsCsv` is absent. | Keep until all rows have `RoleIdsCsv` and API/claim generation can resolve names from `IBeamRoles`. |
| `RolesCsv` | `IBeamUserTenants` | Returned in `TenantInfo.Roles` and kept in sync with `IBeamTenantUsers`. | Same as above. |
| `RoleNamesCsv` | `IBeamPermissionRoleMaps` | Used by permission authorizer grant resolution. | Keep unless permission mapping moves fully to role ids. |
| `RoleNamesCsv` | `IBeamApiCredentials` | Used to build credential access context and claims. | Keep unless credential claims move fully to role ids. |
| `TenantDisplayName` | `IBeamUserTenants` | Used as fallback when `IBeamTenants` does not have a name. | Keep unless tenant rows are guaranteed and fallback is removed. |

### Do not review as IBeam-owned cleanup

| Field/group | Table | Reason |
|---|---|---|
| `LockoutEnd`, `LockoutEndDateUtc`, `LockoutEnabled`, `AccessFailedCount` | `IBeamIdentityUsers` | Provider/ASP.NET Identity fields. IBeam may not rely on them for lockout, but ElCamino owns the table shape. |
| `KeyVersion`, provider index payloads, provider role claim fields | `IBeamIdentityUsers`, `IBeamIdentityRoles`, `IBeamIdentityIndex` | Provider-managed ElCamino fields. Avoid schema cleanup here unless changing provider configuration. |

## Role Field Guidance

There are three separate role concepts in the current schema:

1. `IBeamRoles` is the tenant role catalog and should be treated as canonical for tenant roles.
2. `RoleIdsCsv` on membership and API credential rows is the more stable assignment representation.
3. `RolesCsv` / `RoleNamesCsv` are still used for names, claims, display, and compatibility.

A future cleanup path would be:

1. Make `RoleIdsCsv` the only authorization-critical membership field.
2. Resolve role names from `IBeamRoles` at read/claim time.
3. Keep role names only where they are intentionally part of the public API or credential claims.
4. Migrate rows with empty `RoleIdsCsv` before removing `RolesCsv` fallback behavior.
