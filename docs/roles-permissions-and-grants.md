# IBeam Roles, Permission Maps, and Access Grants

This document explains how IBeam applies roles and grants to users, API credentials, and authorization checks.
It is written for product teams and agents that need to understand the model before changing schema, services, or policies.

## Plain-English Model

IBeam separates three ideas:

| Concept | Plain-English meaning | Example |
|---|---|---|
| Role | A label assigned to a principal inside a tenant. | `Owner`, `Administrator`, `BillingManager` |
| Permission map | A rule that says which roles grant a permission. | `users.manage` is granted by `Owner` and `Administrator` |
| Access grant | A direct grant from a subject to a specific resource. | `user:123` has `edit` access to `project:abc` |

Roles do not directly point at every resource. They usually grant permissions or imply module access.
Access grants are direct resource-level records.

## Role Storage

`IBeamRoles` is the canonical tenant role catalog.

Important fields:

- `TenantId`
- `RoleId`
- `Name`
- `NormalizedName`
- `IsSystem`
- `Status`

Role assignments are mirrored into membership/API credential rows:

| Principal type | Table | Stable field | Compatibility/display field |
|---|---|---|---|
| User | `IBeamTenantUsers` | `RoleIdsCsv` | `RolesCsv` |
| User | `IBeamUserTenants` | `RoleIdsCsv` | `RolesCsv` |
| API credential | `IBeamApiCredentials` | `RoleIdsCsv` | `RoleNamesCsv` |

`RoleIdsCsv` is the better authorization-critical field because ids survive role renames.
Role names are still used for display, claims, compatibility, API scopes, tools, and agent helpers.

## Applying Roles to Users

User role assignment flows through `ITenantRoleService`.

Typical flow:

1. A tenant role exists in `IBeamRoles`.
2. A user is linked to a tenant.
3. Role ids are granted to that user.
4. IBeam stores the assignment in both membership directions.
5. On login, IBeam emits role name and role id claims.

Code example:

```csharp
await tenantRoleService.EnsureTenantMembershipAndGrantRolesAsync(
    new TenantMembershipRoleBootstrapRequest(
        TenantId: tenantId,
        UserId: userId,
        TenantName: "Contoso Health",
        RoleIds: [administratorRoleId],
        RoleNames: ["Administrator"],
        SetAsDefault: true,
        UserDisplayName: "Avery Admin",
        UserEmail: "avery@contoso.test"),
    ct);
```

Grant more role ids to an existing tenant user:

```csharp
await tenantRoleService.GrantRolesAsync(
    tenantId,
    userId,
    [billingManagerRoleId],
    ct);
```

Revoke role ids:

```csharp
await tenantRoleService.RevokeRolesAsync(
    tenantId,
    userId,
    [billingManagerRoleId],
    ct);
```

When the user authenticates, the access token should contain claims like:

```csharp
new Claim("tid", tenantId.ToString("D"));
new Claim("role", "Administrator");
new Claim("rid", administratorRoleId.ToString("D"));
new Claim("role_id", administratorRoleId.ToString("D"));
```

IBeam can evaluate roles from either role-name claims or role-id claims.

## Applying Roles to API Credentials

API credentials are principals too. They can have:

- tenant role ids
- role names
- API scope markers
- tool markers
- allowed agent markers

Create an API credential with tenant roles:

```csharp
var created = await apiCredentialService.CreateAsync(
    tenantId,
    new CreateApiCredentialRequest
    {
        DisplayName = "Billing Import Worker",
        RoleIds = [billingImporterRoleId],
        RoleNames = ["BillingImporter"],
        ExpiresUtc = DateTimeOffset.UtcNow.AddMonths(6)
    },
    createdByUserId,
    ct);
```

Update roles:

```csharp
await apiCredentialService.UpdateRolesAsync(
    tenantId,
    credentialId,
    new UpdateApiCredentialRolesRequest
    {
        RoleIds = [billingImporterRoleId],
        RoleNames = ["BillingImporter"]
    },
    ct);
```

Update API-oriented access:

```csharp
await apiCredentialService.UpdateAccessAsync(
    tenantId,
    credentialId,
    new UpdateApiCredentialAccessRequest
    {
        ApiScopes = ["work"],
        ToolScopes = ["mcp"],
        AllowedAgentKeys = ["billing-agent"],
        RoleIds = [billingImporterRoleId]
    },
    ct);
```

Internally, API scope/tool/agent entries are represented as role-name style markers:

- `api-scope:work`
- `tool:mcp`
- `api-agent:billing-agent`

When the API key authenticates, IBeam creates a claims principal with:

```csharp
new Claim("tid", tenantId.ToString("D"));
new Claim("api_credential_id", credentialId.ToString("D"));
new Claim("api_subject_type", "credential");
new Claim("role", "BillingImporter");
new Claim("rid", billingImporterRoleId.ToString("D"));
new Claim("scope", "work");
new Claim("tool", "mcp");
new Claim("allowed_agent_key", "billing-agent");
```

## Permission Maps

A permission map answers:

> Which roles grant this permission?

Example:

```text
Permission: users.manage
Allowed roles: Owner, Administrator
Allowed role ids: ownerRoleId, administratorRoleId
```

Code example:

```csharp
await permissionStore.UpsertByPermissionNameAsync(
    tenantId,
    permissionName: "users.manage",
    roleNames: ["Owner", "Administrator"],
    roleIds: [ownerRoleId, administratorRoleId],
    ct);
```

Then an access check can ask:

```csharp
var allowed = await accessControl.HasPermissionAsync(
    principal,
    "users.manage",
    ct);
```

The authorization logic is:

```csharp
var grants = await permissionGrantResolver.ResolveAsync(
    tenantId,
    permissionNames: ["users.manage"],
    permissionIds: [],
    ct);

var roleNames = principal.Claims
    .Where(x => x.Type == "role" || x.Type == ClaimTypes.Role)
    .Select(x => x.Value);

var roleIds = principal.Claims
    .Where(x => x.Type == "rid" || x.Type == "role_id")
    .Select(x => Guid.Parse(x.Value));

var allowed =
    grants.RoleNames.Any(roleNames.Contains) ||
    grants.RoleIds.Any(roleIds.Contains);
```

Permission maps do not assign roles to users. They assign permissions to roles.

## Access Grants

An access grant answers:

> Does this specific subject have access to this specific resource?

Grant example:

```text
Tenant: tenantId
Subject: user:userId
Resource: project:projectId
Access: edit
```

Code example:

```csharp
await accessGrantStore.UpsertGrantAsync(
    tenantId,
    grantId: null,
    subjectType: "user",
    subjectId: userId.ToString("D"),
    resourceType: "project",
    resourceId: projectId.ToString("D"),
    accessLevel: "edit",
    ct);
```

Check access:

```csharp
var allowed = await accessControl.HasResourceAccessAsync(
    principal,
    resourceType: "project",
    resourceId: projectId.ToString("D"),
    minimumAccessLevel: "view",
    ct);
```

IBeam evaluates resource access in this general order:

1. Resolve tenant from the principal.
2. Resolve subject as `user` or `api-credential`.
3. Allow owner/admin unrestricted access if configured.
4. Check direct access grants.
5. Resolve permissions implied by role mappings.
6. Check module definitions.
7. Run custom rule providers.

## Bring Your Own Auth

IBeam authorization does not require IBeam to authenticate the user.
Your auth system can issue compatible claims:

```csharp
new Claim("tid", tenantId.ToString("D"));
new Claim("sub", userId.ToString("D"));
new Claim("uid", userId.ToString("D"));
new Claim("role", "Administrator");
new Claim("rid", administratorRoleId.ToString("D"));
```

IBeam can then evaluate permissions and grants against that principal.

## Cleanup Direction

Recommended future cleanup:

1. Make `RoleIdsCsv` the authorization-critical assignment field.
2. Resolve role names from `IBeamRoles` when building responses/claims.
3. Keep role names only where intentionally public or used as API credential markers.
4. Migrate rows with empty `RoleIdsCsv`.
5. Move permission maps and access-control services into core `IBeam.AccessControl` packages so teams can use them without adopting IBeam Identity authentication.

See `docs/core-access-control-extraction-review.md` for the first extraction boundary and remaining migration steps.

See `docs/service-operation-permissions.md` for service-call authorization, operation-name rules,
optional permission-management APIs, and bring-your-own-auth examples.
