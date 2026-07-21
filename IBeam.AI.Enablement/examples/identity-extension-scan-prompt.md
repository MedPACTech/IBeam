# Consuming App Identity Extension Scan Prompt

Use this prompt in a consuming application that uses IBeam Identity and has app-owned `Users`, `Tenants`, profiles, organizations, workspaces, or similar tables.

```text
You are reviewing a consuming application that uses IBeam Identity. Scan the codebase and verify that app-specific user, tenant, profile, organization, workspace, and preference data follows the IBeam extension/domain-table pattern.

Architecture rule:

API <-- DTO object --> Service <-- Entity --> Repository

IBeam Identity owns authentication and security data only:
- identity user id
- login identifiers such as email, phone, OAuth provider links
- passwords, OTP, sessions, refresh tokens, lockout/attempt state
- tenant membership
- role ids, role names, claims, API credentials, and auth/security metadata

The consuming application owns app/domain profile data:
- Users table/profile rows
- Tenants/Organizations/Workspaces table rows
- display/profile details beyond IBeam security needs
- gamer tag, theme, social handle, avatar URL, preferences, onboarding state
- product-specific tenant fields such as slug, billing/provider ids, logo, address, feature flags, lifecycle state

Tasks:

1. Find all user-like and tenant-like entities, DTOs, repositories, tables, controllers, and services.
2. Classify each table/entity as either:
   - IBeam-owned identity/security data
   - app-owned extension/domain data
   - suspicious mixed ownership
3. Verify app-owned user/profile rows are keyed by IBeam `UserId`, and by `TenantId + UserId` when the value is tenant-specific.
4. Verify app-owned tenant/domain rows are keyed by IBeam `TenantId`.
5. Check whether the app registers IBeam extension hooks:
   - `AddIBeamIdentityUserExtension<TUserExtension, TStore>()`
   - `AddIBeamIdentityTenantExtension<TTenant, TStore>()`
   - `AddIBeamIdentityTenantMetadataProvider<TProvider>()` when app tenant metadata should flow back into identity display responses.
6. Verify extension stores only ensure/create/sync app-owned rows during identity lifecycle events. They should not become generic profile update APIs.
7. Verify user-editable app fields such as `GamerTag`, `Theme`, `SocialHandle`, `AvatarUrl`, preferences, and onboarding flags are updated through typed app services and repositories.
8. Verify API controllers are thin and call services. Controllers should not update repositories directly.
9. Verify services own validation, permissions, logging/auditing, and expected error behavior.
10. Verify repositories persist one entity/table and do not call other repositories or services.
11. Find any app-specific fields added directly to IBeam Identity DTOs, Azure Table identity entities, packaged identity tables, or auth/token models. Mark these as suspicious unless there is a deliberate framework-level reason.
12. Find any duplicated identity data in app tables. It is acceptable to cache display fields for performance, but auth-critical values should still resolve from IBeam Identity.
13. Find any API response DTOs that expose IBeam identity DTOs directly when the app should return an app-owned profile DTO.
14. Find any tenant/user profile update endpoints that bypass operation-tagged services. Recommend service method names such as:
   - `users.profile.updateGamertag`
   - `users.profile.updateTheme`
   - `tenants.profile.update`
15. Produce a report with:
   - table/entity name
   - ownership classification
   - key fields
   - related API endpoint(s)
   - related service(s)
   - related repository/table
   - issues found
   - recommended fix

Expected pattern for user profile data:

API endpoint:
`PUT /api/me/profile/gamertag`

DTO:
`UpdateGamerTagRequest`

Service:
`UserProfileService.UpdateGamerTagAsync(tenantId, userId, gamerTag)`

Repository:
`IUserProfileRepository.SaveAsync(AppUserProfile)`

Table:
`Users`, `AppUsers`, or app-specific name

Keys:
`PartitionKey = TENANT|{tenantId:D}`
`RowKey = USER|{userId:D}`

Fields:
`TenantId`, `UserId`, `DisplayName`, `GamerTag`, `Theme`, `SocialHandle`, `CreatedUtc`, `UpdatedUtc`

Do not move app-specific profile fields into IBeam packaged identity tables or auth DTOs. Keep IBeam reusable and keep app profile/domain behavior in the consuming application's service layer.
```
