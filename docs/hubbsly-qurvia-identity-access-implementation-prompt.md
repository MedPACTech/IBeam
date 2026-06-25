# Hubbsly And Qurvia Identity, Licensing, And AccessControl Prompt

Use this prompt when updating Hubbsly or Qurvia to consume the latest IBeam Identity, Licensing, and AccessControl packages. The goal is to keep tenant authentication, feature licensing, and dynamic resource access separate while giving product teams reusable framework APIs.

## Architecture Rule

Use three layers in this order:

1. **Identity** answers who the subject is, which tenant they are acting in, and which stable tenant roles they have.
2. **Licensing** answers whether the tenant or subject is entitled to use a product feature or module.
3. **AccessControl** answers which specific tenant resources the subject can view, edit, delete, or manage.

Do not create a new tenant role for every project, patient, document, card, work item, or resource. Keep roles stable. Use resource grants for dynamic records.

## Baseline Roles

The product owner/development team should define baseline tenant roles centrally. Recommended defaults:

```text
Owner
Admin
User
```

Role meaning should be controlled by the product implementation, not invented ad hoc by tenants. A tenant admin can assign allowed roles to users, but the app should decide whether tenant admins may create custom roles or mutate permission mappings. Default to secure:

- Platform admins or product configuration define role catalogs.
- Tenant owners/admins assign roles within their own tenant only.
- Tenant users cannot manage other tenants.
- Platform/admin/support roles are not tenant-assignable.
- API credential roles are narrower than human roles and validated separately.

Use Identity roles for stable authority:

```text
Owner can manage tenant settings, users, and billing-sensitive areas if the app allows it.
Admin can manage ordinary tenant users and operational settings.
User can use assigned features and resources.
```

Use permission mappings for stable actions:

```text
tenant:users:invite
projects:view
projects:update
billing:manage
api-credentials:manage
```

Use AccessControl grants for dynamic records:

```text
user-1 -> project-1 -> view
user-2 -> project-1 -> edit
user-4 -> project-2 -> view
user-4 -> project-3 -> edit
```

For Hubbsly and Qurvia, keep the application access levels to:

```text
view < edit < delete < manage
```

`manage` should be treated as administration of that specific resource scope. For example, `manage` on a product can allow managing grants or settings for that product and its child projects if the product implementation allows it. Avoid using resource grants as a replacement for tenant ownership, platform administration, or billing authority.

## Work Module Resources

Model the Work module as a parent/child resource tree:

```text
module:work
  product:{productId}
    project:{projectId}
```

A product is a tenant-owned work container. It can represent a sellable product, a service, a line of business, or another high-level work grouping. A product should have at least a name, description, created-by user id, created timestamp, status, and tenant id. More fields can be added by Hubbsly or Qurvia later.

A project belongs to exactly one product. Projects are where specific work tasks are created and managed on the board. The application should support viewing all work for a project and all work across projects for a product.

Do not store product and project definitions in IBeam AccessControl. Hubbsly and Qurvia own product and project tables, and those should be separate repositories/tables:

```text
Products
  ProductId
  TenantId
  Name
  Description
  Status
  CreatedByUserId
  CreatedUtc
  UpdatedUtc

Projects
  ProjectId
  TenantId
  ProductId
  Name
  Description
  Status
  CreatedByUserId
  CreatedUtc
  UpdatedUtc
```

IBeam AccessControl only stores grants against generic resource keys. Use `resourceType = "product"` with `resourceId = productId` for product grants, and `resourceType = "project"` with `resourceId = projectId` for project grants.

Recommended grant behavior:

```text
Grant to module:work
  User can see the Work module according to the granted access level.

Grant to product:{productId}
  User can see the product and all projects/work under that product according to the granted access level.

Grant to project:{projectId}
  User can see the parent product shell and only that assigned project/work. This should not imply access to sibling projects under the same product.
```

Register an `IResourceAccessHierarchyResolver` in the host app so IBeam can resolve ancestors when checking access. For example, when checking `project:{projectId}`, the resolver should return `product:{productId}` and optionally `module:work`. This allows a product-level grant to authorize project-level access without creating dynamic roles.

Product visibility from a project grant is a listing/UI rule, not full product access. When building product lists, include products where the user has either a direct product grant or at least one project grant under the product. When checking access to all projects under a product, require a product-level grant.

## Implementation Flow

When a user signs in, Identity issues tenant-scoped claims such as `uid`, `tid`, `role`, and `rid`. When the user performs an operation, check:

```csharp
await identityAuthorizer.RequirePermissionAsync(principal, tenantId, "projects:view", ct);
await licenseAuthorizer.RequireEntitlementAsync(tenantId, subject, "feature:projects", ct);
var access = await resourceAccessAuthorizer.AuthorizeAsync(
    tenantId,
    resourceType: "project",
    resourceId: projectId.ToString("D"),
    subject: new AccessSubject(AccessSubjectTypes.User, userId.ToString("D")),
    requiredAccessLevel: ResourceAccessLevels.View,
    ct);
```

Only continue when all required checks pass. Apps may choose whether `Owner` or `Admin` bypasses resource grants, but make that an explicit product policy. Sensitive resources should usually require explicit grants even for admins.

IBeam can also emit direct user resource grants into tenant-scoped JWTs through the `resource_access` claim. The claim is a JSON payload intended for frontend navigation and UI affordances, not as the only backend authorization check:

```json
{
  "truncated": false,
  "grants": [
    { "resourceType": "product", "resourceId": "product-1", "accessLevel": "manage", "expiresUtc": null },
    { "resourceType": "project", "resourceId": "project-7", "accessLevel": "edit", "expiresUtc": null }
  ]
}
```

The frontend can use this claim to decide which products/projects/actions to show. Backend APIs must still call `IResourceAccessAuthorizer` because grants can expire, be revoked, or inherit through product/project hierarchy after the token was issued.

## API Usage

Expose IBeam Identity APIs for tenant CRUD, tenant users, role assignment, permission mappings, and API credentials. Protect tenant admin endpoints with tenant-scoped authorization: token `tid` must match the route tenant id unless the caller has an explicit platform-admin policy.

Expose IBeam Licensing APIs when the app sells or grants tenant feature access. Licensing should not replace Identity roles or resource grants.

Expose IBeam AccessControl APIs for grant management and access checks:

```text
GET    /api/tenants/{tenantId}/access-control/grants
POST   /api/tenants/{tenantId}/access-control/grants
PUT    /api/tenants/{tenantId}/access-control/grants/{grantId}
DELETE /api/tenants/{tenantId}/access-control/grants/{grantId}
POST   /api/tenants/{tenantId}/access-control/check
```

Host apps still own domain tables, UX, and typed extension data such as tenant address, logo, patient details, projects, contacts, or work items. IBeam can ensure/sync tenant extension rows, but Hubbsly and Qurvia should expose their own typed endpoints for app-specific fields.

## Migration Guidance

If Hubbsly or Qurvia currently has app-specific `TenantMemberships`, keep it as a domain projection while migrating authorization checks toward IBeam Identity and AccessControl. The source of truth for authentication and tenant role claims should be IBeam Identity. The source of truth for per-resource access should be IBeam AccessControl. The app-specific membership table can remain for UI projections, reporting, or backward compatibility until it is no longer needed.
