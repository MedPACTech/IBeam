# Identity And Licensing Implementation Prompt

Use this prompt when implementing tenant user invitations, Identity membership, role assignment, optional Licensing seat assignment, and dynamic resource access in an IBeam-based application. Keep the architecture modular: `IBeam.Identity` must work without `IBeam.Licensing` or `IBeam.AccessControl`; `IBeam.Licensing` must add entitlement and seat behavior without becoming part of authentication; and `IBeam.AccessControl` must handle per-resource grants without knowing host app domain models.

## Implementation Goal

Build a tenant invitation and user onboarding flow that lets an authorized tenant administrator invite or add a user, verify that user through OTP or email-password setup, assign tenant roles, optionally assign or reserve a license seat, and optionally grant access to dynamic resources. Identity answers who the subject is, which tenant they belong to, and which roles or permissions they have. Licensing answers whether the tenant has a valid license, which entitlements are available, and whether a user, agent, or credential has a seat. AccessControl answers which specific tenant resources a subject can view, edit, manage, or own. Do not put license state or resource grant state into the core Identity model.

## Identity Model

Use `IBeam.Identity` as the source of truth for users, tenants, tenant memberships, roles, permissions, credentials, authentication sessions, and token claims. A successful tenant-scoped token should continue to contain Identity claims such as `uid`, `tid`, `role`, and `rid`. Authorization for inviting users should be based on tenant membership and role or permission checks, such as `owner`, `administrator`, or `admin`, or a host-defined permission like `tenant:users:invite`.

The framework already exposes expanded Identity concepts:

- Users: `IdentityUser` supports user id, email, email confirmation, phone, phone confirmation, display name, two-factor state, and preferred two-factor method.
- Tenants: `IdentityTenant` and `TenantInfo` represent tenant identity, display name, active state, and the user's roles in that tenant.
- Roles: `TenantRole` supports tenant-scoped role ids, names, system roles, active state, and role assignment through `ITenantRoleService`.
- Memberships: `TenantUsers` and `UserTenants` maintain both tenant-to-user and user-to-tenant lookup paths.
- Authentication methods: OTP, email-password registration, password login, two-factor OTP, OAuth linking, API credentials, refresh sessions, and external login mappings are separate Identity capabilities.
- Permissions: permission-to-role mappings allow host apps to expose more specific authorization than role names alone.

For invitations, add reusable Identity primitives in the framework rather than forcing every host app to reinvent token storage and acceptance. Recommended packages: `IBeam.Identity.Invites`, `IBeam.Identity.Invites.Services`, `IBeam.Identity.Invites.Api`, and provider-specific invite stores. The invite service should create, revoke, expire, and accept tenant invitations. Acceptance should resolve or create the user through the configured auth mode, then call `ITenantRoleService.EnsureTenantMembershipAndGrantRolesAsync(...)` with the invite's tenant id, user id, role ids or role names, and default-tenant preference.

Suggested contracts:

```csharp
public interface ITenantUserInviteService
{
    Task<TenantUserInviteInfo> CreateInviteAsync(CreateTenantUserInviteRequest request, CancellationToken ct = default);
    Task<TenantUserInviteAcceptResult> AcceptInviteAsync(AcceptTenantUserInviteRequest request, CancellationToken ct = default);
    Task RevokeInviteAsync(Guid tenantId, Guid inviteId, string? reason = null, CancellationToken ct = default);
}
```

The framework should provide the mechanics. Host applications should provide policy: who can invite, which roles can be granted, whether a domain is allowed, whether approval is required, what messages say, and what UX to show when no license seat is available.

The Identity API should expose reusable tenant, role, and tenant-user operations so host apps do not need to rebuild the same framework calls. Recommended endpoints include tenant create/read/update/activate/deactivate, tenant extension ensure/sync, current-user tenant listing, tenant-user listing, tenant-user lookup, tenant-user linking, default tenant selection, role grant/revoke, and tenant-user disable. These endpoints should operate on Identity-owned data only.

Extended tenant tables are supported through `ITenantExtensionStore<TTenant>` and `ITenantExtensionCoordinator`, but IBeam Identity cannot safely provide a universal update API for app-specific fields because it does not know each host app's schema, validation, storage rules, or file handling requirements. For example, if a host app extends a tenant with physical address and logo fields, the built-in Identity API can ensure the extension row exists and remains synchronized with the Identity tenant, but the host app should provide its own typed endpoint for updating address and logo data. If multiple apps need generic extension CRUD later, introduce a separate opt-in metadata extension package with an explicit dictionary/JSON contract rather than adding arbitrary app data to core Identity.

## Licensing Model

Use `IBeam.Licensing` as an optional entitlement layer. Treat license issuing in three parts:

1. Subscription and billing: payment, renewal, invoice, provider, and commercial lifecycle state.
2. Licenses: tenant-level grants that define plan, status, entitlements, limits, issue date, start date, and expiration date.
3. Seats: subject-level assignments that allow a user, agent, API credential, or external subject to consume a tenant license.

Licenses belong to tenants. Seats are assigned to users or other subjects. A tenant may have one license per user behind the scenes, or one shared license with multiple seats. Subscription and billing should eventually gate license lifecycle, but runtime application access should evaluate the resulting license and seat state.

Licensing must be composed around Identity, not inside it. After invite acceptance creates tenant membership, a licensing-aware host app may run a policy such as:

```csharp
await identityInvites.AcceptInviteAsync(request, ct);
await licensingSeats.AssignSeatAsync(
    tenantId,
    licenseId,
    new AssignLicenseSeatRequest
    {
        Subject = new LicenseSubject(LicenseSubjectTypes.User, userId.ToString("D"))
    },
    createdByUserId: actorUserId,
    ct);
```

Host apps decide the failure mode. If no license seat is available, they may block invite creation, allow membership but block licensed features, allow a pending/unlicensed user state, or require an administrator to choose a different license. Identity should not fail merely because Licensing is absent.

## Dynamic Resource Access Model

Use `IBeam.AccessControl` for dynamic, per-resource grants. Tenant roles should stay stable and human-readable, such as `Owner`, `Admin`, and `User`. Do not create a new tenant role for every project, document, case, card, or workflow item. Instead, keep stable action permissions such as `projects:view` and `projects:update`, then grant resource-level access for specific records.

Example:

```text
Tenant role: User
Permission: projects:view
Resource grant: user-1 can view project-1
Resource grant: user-4 can edit project-2
Resource grant: user-4 can edit project-3
```

The runtime check should compose layers:

```csharp
await identityAuthorizer.RequirePermissionAsync(principal, tenantId, "projects:view", ct);
await licenseAuthorizer.RequireEntitlementAsync(tenantId, subject, "feature:projects", ct);
await resourceAccessAuthorizer.AuthorizeAsync(
    tenantId,
    resourceType: "project",
    resourceId: projectId.ToString("D"),
    subject: new AccessSubject(AccessSubjectTypes.User, userId.ToString("D")),
    requiredAccessLevel: ResourceAccessLevels.View,
    ct);
```

AccessControl should use generic fields: tenant id, resource type, resource id, subject type, subject id, access level, status, expiration, and metadata. Host apps still own the actual resource tables and decide whether tenant admins, owners, or explicit grants can see or modify each resource.

## Table And Schema Guidance

Yes, automatically create required framework tables for installed providers instead of waiting for first data writes. This makes startup behavior predictable, catches configuration errors early, and avoids runtime failures on the first invite, login, role grant, OTP challenge, or license check. The Azure Table Identity provider already follows this pattern with schema application for ElCamino Identity tables and custom IBeam Identity tables.

For `IBeam.Identity.Repositories.AzureTable`, required tables should be created by schema management when the provider is installed and schema mode is `Apply`: `AspNetUsers`, `AspNetRoles`, `AspNetIndex`, `Tenants`, `TenantUsers`, `UserTenants`, `Roles`, `OtpChallenges`, `AuthIdentifiers`, `ExternalLogins`, `AuthSessions`, `ApiCredentials`, `PermissionRoleMaps`, `AuthAttempts`, `SystemLogs`, `SystemErrors`, and `Schema`. New invite tables should follow the same pattern, such as `TenantInvites` and any invite audit/index tables needed for lookup by token, destination, tenant, and status.

Do not create tables for packages that are not installed. Identity should create Identity tables. Licensing providers should create Licensing tables. Invite providers should create Invite tables. This preserves separation while still giving each installed framework package reliable schema readiness. In production, allow `ValidateOnly` or `None` schema modes for teams that require migrations or infrastructure provisioning outside the app process.

## Acceptance Criteria

Implement the flow so an authorized tenant admin can create an invite, the invited user can accept by OTP or email-password setup, the user is created or resolved, tenant membership and roles are assigned through Identity, and optional Licensing policy can assign or reserve a seat without changing Identity contracts. Add tests for admin authorization, invite expiration/revocation, existing-user acceptance, new-user acceptance, OTP acceptance, email-password acceptance, role assignment, tenant membership creation, and licensing-aware host policy behavior when seats are available or unavailable.
