# Application Licensing Outline

## Objective

Provide an IBeam licensing framework that lets host applications sell, grant, renew, and assign tenant-scoped licenses while keeping licensing separate from authentication and tenant membership.

License issuing should be treated as three related parts:

1. Subscription and billing: the commercial source of truth for purchases, renewals, cancellations, invoices, provider references, and payment status.
2. Licenses: tenant-level grants that describe what a tenant can access and when that access is valid.
3. Seats: subject-level assignments that describe which users, agents, or credentials can consume a tenant license.

Subscriptions will eventually become the gatekeeper for license lifecycle, but licensing should not require every license to be renewed by the same provider, schedule, or payment flow. Some licenses may be issued manually, renewed by contract, granted by support, or synchronized from a billing provider at different intervals.

Licensing should answer:

- Which product features or modules has this tenant purchased?
- How many seats or usage units are available?
- Which users or agents consume those entitlements?
- What limits apply to requests, features, storage, tools, modules, or workflows?

Identity should continue to answer:

- Who is the user, agent, or API credential?
- Which tenant are they acting in?
- Which roles, permissions, or API scopes do they have?

## Recommended Package Shape

Create a separate package family:

```text
IBeam.Licensing
IBeam.Licensing.Services
IBeam.Licensing.Api
IBeam.Licensing.Repositories.AzureTable
IBeam.Licensing.Repositories.EntityFramework
```

Dependency direction:

```text
IBeam.Licensing.Api
  -> IBeam.Licensing.Services
      -> IBeam.Licensing
```

Provider packages depend on `IBeam.Licensing` contracts and are composed by the host app.

## Core Concepts

### Subscription And Billing

A subscription or billing record represents the commercial relationship behind one or more licenses.

It may include provider information, invoice state, renewal rules, payment status, contract terms, or other purchase metadata. Billing can determine whether a license should be issued, renewed, suspended, or revoked, but runtime authorization should evaluate the resulting license and seat state.

Subscription and billing should be modeled separately because not every license will renew the same way. A host app may have:

- monthly self-service subscriptions
- annual contract renewals
- manually granted trial or partner licenses
- support-issued extensions
- externally synchronized provider subscriptions

### Product

A product is a sellable application or module group.

Examples:

- Hubbsly
- Hubbsly Work
- Hubbsly Money
- Hubbsly Contacts

### License Plan

A plan defines a commercial offering and its default entitlements.

Examples:

- Starter
- Professional
- Enterprise
- Work-only
- Money close add-on

### Tenant License

A tenant license is an active purchase or grant assigned to one tenant.

Licenses belong to tenants. They can be backed by a subscription, a billing provider, a manual grant, or another issuing process. A license should have an issue date and an expiration date. The expiration date can be updated when the license is renewed, extended, corrected, or synchronized from an external source.

A tenant may have one license per user behind the scenes, even when those users belong to a single tenant. A tenant may also have one shared license, or one type of license, with multiple purchased seats assigned to individual users.

It should include:

- `tenantId`
- `licenseId`
- `planKey`
- `status`
- `issuedUtc`
- `startsUtc`
- `expiresUtc`
- `seatLimit`
- `usageLimits`
- `featureEntitlements`
- `metadata`

### Entitlement

An entitlement is a feature, module, capability, or usage right granted by a license.

Examples:

```text
feature:work
feature:contacts
feature:money
mcp:tools
work:cards:create
money:close:update
contacts:communications:log
```

### Seat Assignment

A seat assignment links a tenant license to a user, API credential, or agent.

Seats are issued to users or other license subjects. A license can be effectively one-to-one with a user by issuing one license and one seat for that user, or it can be one-to-many by issuing one tenant license with multiple available seats.

Seat assignment should be the mechanism that determines who can consume a tenant license. Tenant membership alone should not imply license access unless the host app explicitly configures an automatic seat assignment policy.

Assignment subject types:

```text
user
api-credential
agent
external
```

### Usage Limit

A usage limit controls quantity-based access.

Examples:

- max users
- max agents
- max API credentials
- max MCP calls per month
- max contacts
- max work cards
- max storage GB

## Relationship To Identity

Licensing should integrate with Identity but not be part of Identity.

Identity owns:

- users
- tenants
- tenant memberships
- roles
- permissions
- API credentials
- authenticated claims

Licensing owns:

- subscription and billing references
- plans
- licenses
- entitlements
- seat assignment
- usage limits

Services should be able to check both:

1. Identity authorization: "Can this principal perform this action?"
2. Licensing entitlement: "Has this tenant purchased this capability and does it have remaining capacity?"

When users are added to tenants, Identity should remain responsible for tenant membership and roles. Licensing should provide a follow-up decision point:

- Does this tenant license require a seat for this user?
- Should a seat be assigned automatically when membership is created?
- Should an administrator choose the license or seat type manually?
- Should user creation be blocked, allowed without licensed access, or allowed with limited access when no seat is available?

That integration should be designed after the licensing model is stable, because the correct behavior may differ by host application and license type.

## Authorization Flow

For a protected service operation:

1. Resolve tenant and subject from Identity claims.
2. Check role/permission/API scope through existing IBeam authorization patterns.
3. Check license entitlement for the tenant and subject.
4. Check usage limits when the operation consumes capacity.
5. Execute the operation.
6. Record usage if needed.

Example:

```csharp
await _roleAccess.AuthorizeAsync(user, "work:write", ct);
await _licenseAuthorizer.RequireEntitlementAsync(
    tenantId,
    subject,
    "work:cards:create",
    ct);
```

## Proposed Contracts

```csharp
public interface ILicensePlanCatalogProvider
{
    Task<IReadOnlyList<LicensePlanInfo>> ListPlansAsync(CancellationToken ct = default);
}

public interface ITenantLicenseService
{
    Task<IReadOnlyList<TenantLicenseInfo>> ListTenantLicensesAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantLicenseInfo> GrantLicenseAsync(Guid tenantId, GrantTenantLicenseRequest request, CancellationToken ct = default);
    Task<TenantLicenseInfo> UpdateLicenseAsync(Guid tenantId, Guid licenseId, UpdateTenantLicenseRequest request, CancellationToken ct = default);
    Task RevokeLicenseAsync(Guid tenantId, Guid licenseId, string? reason, CancellationToken ct = default);
}

public interface ILicenseSeatAssignmentService
{
    Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<LicenseSeatAssignmentInfo> AssignSeatAsync(Guid tenantId, Guid licenseId, AssignLicenseSeatRequest request, CancellationToken ct = default);
    Task RevokeSeatAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct = default);
}

public interface ILicenseAuthorizer
{
    Task<LicenseAuthorizationResult> AuthorizeAsync(
        Guid tenantId,
        LicenseSubject subject,
        string entitlement,
        CancellationToken ct = default);
}
```

## Proposed API Endpoints

Admin endpoints:

```http
GET    /api/license-plans
GET    /api/tenants/{tenantId}/licenses
POST   /api/tenants/{tenantId}/licenses
PUT    /api/tenants/{tenantId}/licenses/{licenseId}
POST   /api/tenants/{tenantId}/licenses/{licenseId}/revoke
GET    /api/tenants/{tenantId}/licenses/{licenseId}/assignments
POST   /api/tenants/{tenantId}/licenses/{licenseId}/assignments
DELETE /api/tenants/{tenantId}/licenses/{licenseId}/assignments/{assignmentId}
```

Runtime/introspection endpoints:

```http
GET  /api/tenants/{tenantId}/license-entitlements
POST /api/tenants/{tenantId}/license-entitlements/check
```

## Configuration

Host apps should be able to add plans and entitlements through configuration or code.

Example:

```json
{
  "IBeam": {
    "Licensing": {
      "Plans": [
        {
          "Key": "hubbsly-work",
          "DisplayName": "Hubbsly Work",
          "Entitlements": [
            "feature:work",
            "work:cards:create",
            "work:cards:update",
            "mcp:tools"
          ],
          "Limits": {
            "Seats": 4,
            "McpCallsPerMonth": 10000
          }
        }
      ]
    }
  }
}
```

## Service Layer Rules

1. Licensing checks live in services, not only controllers.
2. Tenant boundary checks must happen before license checks.
3. License checks should not replace role/permission checks.
4. Usage counters should be transactional where possible.
5. Licensing failures should be distinguishable from authorization failures.
6. Host apps should be able to override plan catalogs, billing mapping, and usage storage.

## AI And MCP Integration

MCP tools can require both API credential scopes and tenant license entitlements.

Example:

```text
API credential scope: api-scope:work
License entitlement: work:cards:create
```

`IBeam.Ai` should continue checking principal scopes. Host tool handlers or app services should call `ILicenseAuthorizer` for entitlements.

Future enhancement:

- Add optional `RequiredEntitlements` metadata to `AgentToolDefinition`.
- Provide an `IAgentToolAccessPolicy` implementation that checks both Identity scopes and Licensing entitlements.

## Billing Provider Integration

Licensing should not require a billing provider, but should allow one.

Provider reference fields:

```text
providerName
providerCustomerId
providerSubscriptionId
providerPriceId
providerStatus
providerCurrentPeriodStartUtc
providerCurrentPeriodEndUtc
```

Billing providers should update license state rather than replacing it. For example, a successful renewal may update `expiresUtc`, while a failed payment may eventually suspend or revoke the affected license. Manual licenses can use the same license dates without having a provider subscription.

Potential provider packages:

```text
IBeam.Licensing.Stripe
IBeam.Licensing.Paddle
```

## Deferred Identity Integration Work

After the license, subscription, and seat model is stable, build out the rules for users being added to tenants.

See [Identity And Licensing Implementation Prompt](identity-licensing-implementation-prompt.md) for a handoff-ready implementation brief covering Identity invitations, expanded users/roles/tenants, optional license seat policy, and schema/table guidance.

Key decisions:

- whether tenant membership automatically attempts to assign a license seat
- whether seat assignment is tied to a default tenant license, a selected plan type, or an administrator action
- whether users can belong to a tenant without consuming a license
- how failed seat assignment should appear in Identity and application UX
- how seat assignment changes should be reflected in claims, authorization checks, or tenant membership views

The integration should keep Identity as the source of truth for membership while Licensing remains the source of truth for entitlement and seat consumption.

## Suggested First Implementation Slice

1. Create `IBeam.Licensing` contracts and models. Implemented.
2. Create `IBeam.Licensing.Services` with in-memory/no-op catalog support. Implemented.
3. Create `IBeam.Licensing.Api` with plan and tenant-license endpoints. Implemented.
4. Add Azure Table provider after service contracts stabilize.
5. Add EF provider if host apps need relational persistence.
6. Add optional AI/MCP access policy integration after base licensing checks work.

## Tests

Minimum tests:

- Plan catalog returns configured plans.
- Tenant license grant stores entitlements and limits.
- Seat assignment enforces seat limit.
- License authorizer allows active entitlement.
- License authorizer denies missing, expired, revoked, or over-limit licenses.
- Identity role checks remain separate from license checks.
- MCP/tool scenarios can combine API credential scopes and license entitlements.
