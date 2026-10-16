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

For selected password/MFA/refresh/logout endpoints, reference IBeam.Management.Identity.Api and call AddIBeamManagementIdentityApi instead of registering these services twice. Do not expose the generic Identity controller assembly in the management host. The adapter blocks generic enrollment, recovery, tenant selection and MFA changes even through IIdentityAuthService; invitation onboarding is a later feature. Every protected request and refresh checks current membership, role catalog and persisted MFA session without caching. Only ManagementAdministrator may disable another support membership and revoke its sessions.
