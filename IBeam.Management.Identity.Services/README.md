# IBeam.Management.Identity.Services
Optional isolated IBeam Identity integration for a dedicated management host. Management core and API packages remain usable with bring-your-own authentication.

Call services.AddIBeamManagementIdentityServices(configuration) in a separate service collection/host, before adding the Management API. Register the host's communication sender for email MFA separately. Registration rejects an already registered Identity user provider and does not auto-run schema creation or account bootstrap.

Configuration uses IBeam:Management:Identity:
- OrganizationId: a fixed nonempty support-organization GUID.
- Storage:Mode: Separate (default) or Shared.
- Storage:ConnectionString: a secret-backed storage connection. Separate requires this explicitly; it never falls back to customer credentials.
- Storage:TablePrefix: Management by default; applies to every provider table, index, session, OTP, schema metadata, logging and access-control table.
- Shared mode additionally requires IBeam:Identity:AzureTable:StorageConnectionString and the existing customer table settings for endpoint/collision checks. A dedicated management connection for that account may be supplied.
- BootstrapEnabled: false in runtime hosts; enable only in trusted one-off setup.
- Identity options (Jwt, Otp and so on) are read from this management section, not inherited from the customer Identity section.

A separate account/credential scope is the stronger isolation option. A shared account with different prefixes gives logical separation; an account-wide key can still reach all tables. Prefixes are never an authorization boundary. The current connection-string provider cannot promise prefix-scoped RBAC. Operators must choose credentials/account boundaries appropriate to their threat model and must not give the runtime schema/management privileges merely for convenience.

Trusted setup:
1. In one-off deployment tooling only, set Storage:ProvisionSchema=true, resolve IIdentitySchemaManager and call ApplyAsync with schema privileges. Runtime defaults remain false.
2. Configure BootstrapEnabled=true only in that tooling; resolve IManagementIdentityBootstrapService and call BootstrapAsync with organization name, administrator email and an initial password supplied from an environment variable/secret input. No bootstrap HTTP endpoint exists.
3. Reruns reuse the same user, organization and role without resetting the password. Disabled organizations/memberships are never resurrected. A newly created administrator has email MFA enabled.
4. Set BootstrapEnabled=false and Storage:ProvisionSchema=false, and use a runtime connection without schema-creation privileges. Runtime registration does not run the provider's schema hosted service and CreateTablesIfNotExists=false.

Management users, OTP, indexes and sessions are independent from customer identities even if emails or generated user IDs coincide. The support organization is not a customer tenant.

For selected password/MFA/refresh/logout endpoints, reference IBeam.Management.Identity.Api and call AddIBeamManagementIdentityApi instead of registering these services twice. Do not expose the generic Identity controller assembly in the management host. The adapter blocks generic enrollment, recovery, tenant selection and MFA changes even through IIdentityAuthService; controlled invitation onboarding is supplied by the separate support administration module. Every protected request and refresh checks current membership, role catalog and persisted MFA session without caching. Only ManagementAdministrator may disable another support membership and revoke its sessions.

## Application administration permission

The exact live ManagementAdministrator catalog role grants management.applications.admin in addition to management.identity.read. ManagementSupport grants identity.read only. Trusted MFA issuance and every protected request/refresh rebuild these permissions from live membership roles, stripping stale claims; removing administrator access therefore takes effect on the next request. Customer Owner/Admin roles never grant application administration.

## Support directory and invitations (IBM-0089)

IManagementSupportUserService reuses the management Identity directory, TenantInvites, existing users, memberships and role stores. No separate support profile/auth table is created. Explicit management.support-users.admin plus a live ManagementAdministrator membership/session is required for listing, invitations and disablement.

Invitations are email-only, expire after 48 hours and grant ManagementSupport only. Configure IBeam:Management:Identity:Invites:Links:DefaultAcceptUrl to the trusted portal acceptance page; host delivery uses the existing Identity sender. Portal fields cannot choose organization, roles, identity email, default-tenant behavior or redirect links. Only invitation metadata is returned, never the token/link.

The email-delivered token identifies the intended address. Acceptance verifies the canonical invite, expiry, fixed role and still-active issuing administrator. Existing identities must supply their current password and cannot already have a management membership (disabled memberships are not reenabled). New passwords use the existing Identity provider policy. Acceptance confirms that address, enables email MFA and links only the management organization. It issues no access/refresh/pre-tenant token; users must subsequently complete normal password + MFA sign-in.

A single-use atomic marker is added to the existing prefixed TenantInvites table under MGTREDEEM|organization. It contains invite/user IDs and time only; this prevents simultaneous replay across host instances. The marker is never released after a partial failure: an administrator must issue a fresh invitation. Existing membership/password state is not reset by replay.

Routine disablement is membership-first followed by session revocation. Every protected request/refresh rechecks membership, so partial revocation failure remains denied. Self-disable and disabling any management administrator are rejected, including on the legacy disable route; administrator recovery/removal stays explicit trusted tooling. This prevents simultaneous administrative disables from removing the final administrator.

## App-assignment permission mapping

The exact live ManagementAdministrator role additionally supplies management.application-access.admin and the errors/logs delegation permissions. Claims are rebuilt at MFA issuance and each protected request/refresh; removed roles lose those grants immediately. ManagementSupport receives no administrative or delegation permission. IManagementSupportUserStateProvider resolves current organization status, user MFA, membership and live catalog roles for assignment checks. Application assignments remain in their own management table rather than Identity role/user profile rows.

Deployment configuration, identity storage choices, MFA/bootstrap, onboarding, exact registrations/endpoints, network/proxy policy, secret rotation, audit retention/failure behavior and the deferred consumer host are covered by the [Management deployment handoff](https://github.com/MedPACTech/IBeam/blob/development/docs/management/deployment-and-handoff.md). [Source/history isolation](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-source-isolation.md) describes which historical rows may be exposed. Generated-package validation uses the package-only two-target smoke sample; IBM-0097 configurable scrubbing remains deferred.
