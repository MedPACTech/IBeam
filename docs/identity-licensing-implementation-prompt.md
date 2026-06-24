# Identity And Licensing Implementation Prompt

Use this prompt when implementing tenant user invitations, Identity membership, role assignment, and optional Licensing seat assignment in an IBeam-based application. Keep the architecture modular: `IBeam.Identity` must work without `IBeam.Licensing`, and `IBeam.Licensing` must add entitlement and seat behavior without becoming part of authentication.

## Implementation Goal

Build a tenant invitation and user onboarding flow that lets an authorized tenant administrator invite or add a user, verify that user through OTP or email-password setup, assign tenant roles, and optionally assign or reserve a license seat. Identity answers who the subject is, which tenant they belong to, and which roles or permissions they have. Licensing answers whether the tenant has a valid license, which entitlements are available, and whether a user, agent, or credential has a seat. Do not put license state into the core Identity model or require Licensing for Identity-only apps.

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

## Table And Schema Guidance

Yes, automatically create required framework tables for installed providers instead of waiting for first data writes. This makes startup behavior predictable, catches configuration errors early, and avoids runtime failures on the first invite, login, role grant, OTP challenge, or license check. The Azure Table Identity provider already follows this pattern with schema application for ElCamino Identity tables and custom IBeam Identity tables.

For `IBeam.Identity.Repositories.AzureTable`, required tables should be created by schema management when the provider is installed and schema mode is `Apply`: `AspNetUsers`, `AspNetRoles`, `AspNetIndex`, `Tenants`, `TenantUsers`, `UserTenants`, `Roles`, `OtpChallenges`, `AuthIdentifiers`, `ExternalLogins`, `AuthSessions`, `ApiCredentials`, `PermissionRoleMaps`, `AuthAttempts`, `SystemLogs`, `SystemErrors`, and `Schema`. New invite tables should follow the same pattern, such as `TenantInvites` and any invite audit/index tables needed for lookup by token, destination, tenant, and status.

Do not create tables for packages that are not installed. Identity should create Identity tables. Licensing providers should create Licensing tables. Invite providers should create Invite tables. This preserves separation while still giving each installed framework package reliable schema readiness. In production, allow `ValidateOnly` or `None` schema modes for teams that require migrations or infrastructure provisioning outside the app process.

## Acceptance Criteria

Implement the flow so an authorized tenant admin can create an invite, the invited user can accept by OTP or email-password setup, the user is created or resolved, tenant membership and roles are assigned through Identity, and optional Licensing policy can assign or reserve a seat without changing Identity contracts. Add tests for admin authorization, invite expiration/revocation, existing-user acceptance, new-user acceptance, OTP acceptance, email-password acceptance, role assignment, tenant membership creation, and licensing-aware host policy behavior when seats are available or unavailable.
