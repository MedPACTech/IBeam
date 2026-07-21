# IBeam 2.8 Consuming API Upgrade Prompt

Use this prompt with an AI coding agent in a consuming API repository after upgrading to IBeam 2.8.0.

```text
You are updating an existing consuming API codebase to adopt IBeam 2.8.0.

Your goal is to bring in tenant invitations, tenant-managed user onboarding, configurable Identity API management authorization, and tenant-scoped API credential management without changing unrelated product behavior.

Treat the consuming API as the host application. IBeam owns reusable identity/access infrastructure. The host app owns domain-specific roles, modules, resources, licensing, billing, screens, product rules, and final enforcement in its own services.

## First Steps

1. Inspect the consuming repo before editing.
2. Read existing `AGENTS.md`, `.agent`, `.ai`, README, package prompts, and architecture docs.
3. Inspect current IBeam package references and versions.
4. Identify whether the app uses:
   - `IBeam.Identity.Api`
   - `IBeam.Identity.Services`
   - `IBeam.Identity.Repositories.AzureTable`
   - `IBeam.AccessControl`
   - `IBeam.AccessControl.Services`
   - `IBeam.AccessControl.Repositories.AzureTable`
   - API credentials
   - tenant roles, permission maps, or resource grants
   - custom user/tenant extension tables
5. Keep changes scoped. Do not rewrite unrelated auth, billing, UI, or domain workflows.

## Target Package Baseline

Update IBeam package references to `2.8.0` where the consuming app references IBeam packages.

Relevant packages commonly needed for an Identity-backed API:

- `IBeam.Identity`
- `IBeam.Identity.Services`
- `IBeam.Identity.Api`
- `IBeam.Identity.Repositories.AzureTable`
- `IBeam.AccessControl`
- `IBeam.AccessControl.Services`
- `IBeam.AccessControl.Repositories.AzureTable`
- `IBeam.Communications`
- `IBeam.Communications.Email.AzureCommunications`
- `IBeam.Communications.Sms.AzureCommunications`

Only add packages the app actually needs. If the app uses a different persistence provider or communications provider, preserve that design.

## Dependency Injection

For a standard IBeam Identity API host, ensure startup has the normal Identity API registration:

```csharp
builder.Services.AddIBeamIdentityApi(builder.Configuration);
builder.Services.AddIBeamIdentityApiControllers();
```

If the app registers Identity services more manually, ensure the equivalent service, invite, API credential, role, access-control, and repository registrations are present.

If using Azure Table storage for Identity, ensure:

```csharp
builder.Services.AddIBeamIdentityAzureTable(builder.Configuration);
```

If the app registers AccessControl Azure Table stores directly, ensure:

```csharp
builder.Services.AddIBeamAccessControlAzureTableStores(builder.Configuration);
```

Do not register obsolete Identity-owned access-control stores. AccessControl owns resource grants, permission-role maps, and service-operation permission rules.

## Tenant Invitation Support

Add or verify tenant invite support in the host API.

Expected Identity API endpoints:

```text
POST /api/tenants/{tenantId}/invites
GET  /api/tenants/{tenantId}/invites
GET  /api/tenants/{tenantId}/invites/{inviteId}
POST /api/tenants/{tenantId}/invites/{inviteId}/resend
POST /api/tenants/{tenantId}/invites/{inviteId}/revoke
GET  /api/invites/{tokenOrCode}/preview
POST /api/invites/accept
```

The consuming app should decide whether to expose IBeam controllers directly or wrap them with app-specific routes. Keep wrappers thin.

Host responsibilities:

- Invite screens and routes.
- Branded email/SMS templates.
- Public invite acceptance UI.
- Post-accept profile completion.
- License seat checks before invite creation or acceptance, if the product requires them.
- Domain-specific role choices and default invite roles.
- Any app-specific user or tenant extension records.

Override invite delivery behavior when branding or product URLs are required:

```csharp
builder.Services.AddScoped<ITenantInviteUrlBuilder, AppInviteUrlBuilder>();
builder.Services.AddScoped<ITenantInviteMessageFactory, AppInviteMessageFactory>();
```

Use the app's configured frontend URL for invite links. Do not hard-code localhost or environment-specific domains.

## Tenant Invite Storage And Schema

If using Azure Table storage, verify table provisioning includes tenant invite storage.

Default logical/physical table names to account for:

```text
TenantInvites
```

Check app infrastructure, schema bootstrap scripts, drift checks, deployment scripts, and local seed/reset scripts for missing tenant invite table creation.

Do not store raw invite tokens. IBeam stores token hashes. Client/UI code should treat invite tokens as one-time secrets.

## Configurable Management Authorization

IBeam 2.8.0 removes hard-coded Identity API admin role assumptions from the management controllers. Configure management access under:

```text
IBeam:Identity:AccessControl
```

Default roles remain suitable for small apps:

```json
{
  "IBeam": {
    "Identity": {
      "AccessControl": {
        "OwnerRoleNames": [ "Owner" ],
        "AdminRoleNames": [ "Administrator", "Admin" ]
      }
    }
  }
}
```

For larger apps, choose explicit admin tiers:

```json
{
  "IBeam": {
    "Identity": {
      "AccessControl": {
        "OwnerRoleNames": [ "Owner" ],
        "AdminRoleNames": [ "PlatformAdmin", "TenantAdmin", "ClinicAdministrator" ],
        "TenantManagementPermissionNames": [ "identity.tenants.manage" ],
        "TenantUserManagementPermissionNames": [ "identity.tenantusers.manage", "identity.tenantinvites.manage" ],
        "TenantRoleManagementPermissionNames": [ "identity.tenantroles.manage" ],
        "TenantAccessControlManagementPermissionNames": [ "identity.accesscontrol.manage" ],
        "ApiCredentialManagementPermissionNames": [ "identity.apicredentials.manage" ],
        "AuthAttemptManagementRoleNames": [ "PlatformAdmin", "Support" ],
        "AuthAttemptManagementPermissionNames": [ "identity:auth-attempts:unlock" ]
      }
    }
  }
}
```

The Identity API accepts configured role names from:

```text
role
roles
ClaimTypes.Role
```

It accepts configured permission names from:

```text
permission
permissions
scope
scp
```

It checks tenant identity from:

```text
tid
tenant_id
http://schemas.microsoft.com/identity/claims/tenantid
```

Make sure the consuming app's JWT creation/enrichment emits claims that match its configured admin roles and permission names.

## IBeamOperation And Permissions

IBeam 2.8.0 defaults include broad management permission names and concrete operation names. Prefer stable operation names when building large-app permissions.

Useful built-in operation names include:

```text
identity.tenantinvites.create
identity.tenantinvites.list
identity.tenantinvites.get
identity.tenantinvites.resend
identity.tenantinvites.revoke
identity.tenantroles.create
identity.tenantroles.update
identity.tenantroles.delete
identity.tenantroles.grant
identity.tenantroles.revoke
identity.apicredentials.create
identity.apicredentials.rotate
identity.apicredentials.revoke
identity.apicredentials.activate
identity.apicredentials.access.update
accesscontrol.resourceaccess.grant
accesscontrol.resourceaccess.update
accesscontrol.resourceaccess.revoke
accesscontrol.permissionroles.upsert.name
accesscontrol.permissionroles.upsert.id
```

If the consuming app has permission-role mapping UI or seed data, map app roles to these operation names where appropriate.

Do not hard-code controller-level role checks such as `Admin`, `Administrator`, or `Owner` in app controllers. Use configured IBeam access-control options, service-operation permissions, policies, or thin wrappers around IBeam APIs.

## API Credential Management

Verify API credential management routes and UI/client code understand tenant-scoped credentials:

```text
GET    /api/tenants/{tenantId}/api-credentials
GET    /api/tenants/{tenantId}/api-credentials/{credentialId}
POST   /api/tenants/{tenantId}/api-credentials
PUT    /api/tenants/{tenantId}/api-credentials/{credentialId}
DELETE /api/tenants/{tenantId}/api-credentials/{credentialId}
POST   /api/tenants/{tenantId}/api-credentials/{credentialId}/rotate
POST   /api/tenants/{tenantId}/api-credentials/{credentialId}/revoke
POST   /api/tenants/{tenantId}/api-credentials/{credentialId}/activate
GET    /api/tenants/{tenantId}/api-credentials/{credentialId}/access
PUT    /api/tenants/{tenantId}/api-credentials/{credentialId}/access
GET    /api/api-credentials/role-catalog
GET    /api/tenants/{tenantId}/api-credentials/scope-catalog
```

API credential management is intentionally human-admin only. API key principals should not be allowed to create, rotate, revoke, or expand other API keys even if they carry matching role strings.

Expected API key authentication claims include:

```text
api_subject_type = credential
principal_type = api-credential
api_credential_id = {credentialId}
tid = {tenantId}
tenant_id = {tenantId}
role = API
scope = {apiScope}
```

Use this subject type for resource grants:

```json
"api-credential"
```

Do not use the old or camel-cased subject type:

```json
"apiCredential"
```

## Access-Control Catalog And Grants

Register host modules, permissions, resources, tools, and agents in code/config. IBeam provides the catalog and grant machinery; the host app decides its product vocabulary.

Example:

```csharp
builder.Services.AddIBeamAccessControl(options =>
{
    options.Modules.AddRange(AppModules.All);
    options.ResourceCatalogProviders.Add<AppAccessCatalogProvider>();
});
```

Domain services must still enforce resource relationships. For example, if a credential has access to a project, the app service should still verify that project belongs to the route tenant and requested product/account.

## Client/UI Updates

Update admin UI or API clients for:

- Invite list/create/resend/revoke screens.
- Invite preview and accept screens.
- Tenant user onboarding through invite rather than only direct user linking.
- Configurable admin labels instead of assuming only `Admin`.
- API credential create/rotate/revoke/activate screens.
- API credential role/scope/resource grant assignment.
- Current access context bootstrapping from `/api/access/me` or `/api/tenants/{tenantId}/access-control/me`.

Do not expose raw API keys after creation/rotation. Show once, then require rotation for recovery.

## Tests To Add Or Update

Add or update tests for:

- Host startup/DI resolves IBeam Identity API services.
- Tenant invite create/list/get/resend/revoke is forbidden for non-admin users.
- Tenant invite create succeeds for the configured tenant admin role.
- Tenant invite create succeeds for the configured permission/operation claim when expected.
- Invite preview does not reveal whether a user already exists.
- Invite accept links existing users and creates new users as expected.
- Tenant id mismatch returns forbidden.
- API credential management rejects API-key principals.
- API credential management succeeds for configured human admin role or permission.
- Resource grant payloads use `api-credential`.
- JWT/token creation includes configured role/permission claims.

When tests use fake principals, include realistic claims:

```csharp
new Claim("tid", tenantId.ToString("D"));
new Claim("uid", userId.ToString("D"));
new Claim("role", "TenantAdmin");
new Claim("permission", "identity.tenantinvites.create");
```

## Search Checklist

Search the consuming repo for:

```text
IBeam.
TenantInvites
ITenantInvite
AddIBeamIdentity
AddIBeamAccessControl
OwnerRoleNames
AdminRoleNames
apiCredential
api-credential
owner
administrator
admin
identity.tenant
identity.apicredentials
accesscontrol.
```

Handle findings carefully:

- Replace hard-coded management roles with configuration where they govern IBeam admin endpoints.
- Preserve app-domain roles where they are real product roles.
- Replace `apiCredential` persisted subject/principal values with `api-credential`.
- Add tenant invite support only where the host product wants tenant-managed onboarding.
- Avoid unrelated refactors.

## Final Deliverables

Report:

- IBeam packages updated.
- DI registrations added or changed.
- Access-control/admin configuration added or changed.
- Tenant invite routes/UI/client changes added.
- API credential management changes added.
- Schema/infrastructure changes required.
- Tests added or updated.
- Any app-specific decisions still needed from product owners, such as default invite role, license-seat policy, admin tier names, or branded invite URL.
```
