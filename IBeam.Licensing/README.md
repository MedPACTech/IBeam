# IBeam.Licensing

`IBeam.Licensing` contains the core licensing contracts, request/response models, plan models, subject model, and option objects used by IBeam-backed applications.

Install this package when shared application code needs to understand licensing concepts without taking a dependency on the service or API implementation.

```powershell
dotnet add package IBeam.Licensing
```

## When To Use This

- You need common licensing models in API, service, worker, or integration projects.
- You want to check whether a tenant has an entitlement such as `work:cards:create`.
- You need to represent seats for users, API credentials, agents, or external subjects.
- You are building your own licensing store or API layer and only need contracts.

Licensing is intentionally separate from Identity. Identity answers who the caller is and which roles/scopes they have. Licensing answers whether the tenant purchased or was granted the capability being used.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Plan catalog | `LicenseProductInfo`, `LicensePlanInfo`, `LicensePlanOptions`, `ILicensePlanCatalogProvider` | Defines products, plans, entitlements, limits, default seats, default credit grants, provider price references, and metadata. |
| Tenant licenses | `TenantLicense`, `GrantTenantLicenseRequest`, `UpdateTenantLicenseRequest`, `ITenantLicenseService` | Represents a tenant's active/revoked product license. |
| Seat assignments | `LicenseSeatAssignment`, `AssignLicenseSeatRequest`, `ILicenseSeatAssignmentService` | Links a license to a user, API credential, agent, or external subject. |
| Authorization | `ILicenseAuthorizer`, `LicenseAuthorizationResult` | Checks whether a subject can use an entitlement. |
| Store contract | `ILicensingStore` | Persistence boundary for licenses and seat assignments. |
| Extensibility | `ILicenseExtension` | Hook for provider-specific or app-specific licensing behavior. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is the model/contract layer. It does not own HTTP endpoints, database tables, or provider integrations. Services make licensing decisions, repositories persist licensing data, and APIs only expose service calls.

## Core Concepts

| Concept | Plain-English Meaning |
|---|---|
| Product | The sellable product family a plan belongs to. |
| Plan | The product package a tenant can buy or receive. |
| Plan level | A host-defined ordering such as basic, pro, or enterprise. |
| Entitlement | A named capability, such as `feature:work` or `work:cards:create`. |
| Limit | A numeric quota, such as seats or monthly calls. |
| Credit grant | A plan-provided consumption allowance for a host-defined bucket. |
| Tenant license | A plan granted to one tenant. |
| Seat assignment | A subject consuming a seat on a tenant license. |
| Subject | The thing using the product: user, API credential, agent, or external identity. |

## License Lifecycle

Tenant licenses distinguish runtime grant status from commercial billing state:

| Field | Meaning |
|---|---|
| `Status` | Runtime grant state such as `active`, `trialing`, `grace`, `manual`, `suspended`, `expired`, or `revoked`. |
| `CommercialStatus` | Commercial state such as `paid`, `trial`, `grace`, `past-due`, `canceled`, `manual`, or `support-granted`. |
| `StartsUtc` / `ExpiresUtc` | Runtime validity window for the grant. |
| `GraceEndsUtc` | Optional runtime grace window after expiration. |

Use `EvaluateRuntimeEligibility(now)` when you need a structured runtime decision. `IsActive(now)` remains available for existing consumers and returns the runtime eligibility result.

## Code Example

Typical service-layer usage:

```csharp
public sealed class WorkCardService
{
    private readonly ILicenseAuthorizer _licenses;

    public WorkCardService(ILicenseAuthorizer licenses)
    {
        _licenses = licenses;
    }

    public async Task CreateCardAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        await _licenses.RequireEntitlementAsync(
            tenantId,
            new LicenseSubject(LicenseSubjectTypes.User, userId.ToString()),
            "work:cards:create",
            ct);

        // Continue with the licensed operation.
    }
}
```

Identity and Licensing are often checked together:

```csharp
await _roleAccess.AuthorizeAsync(user, "work:write", ct);
await _licenses.RequireEntitlementAsync(tenantId, subject, "work:cards:create", ct);
```

For API credential and MCP scenarios, treat the API credential scope as the authenticated permission and the license entitlement as the purchased capability:

```text
API credential scope: api-scope:work
License entitlement: work:cards:create
```

## Configuration Shape

Plans are usually configured under `IBeam:Licensing`:

```json
{
  "IBeam": {
    "Licensing": {
      "Products": [
        {
          "Key": "hubbsly",
          "DisplayName": "Hubbsly"
        }
      ],
      "Plans": [
        {
          "Key": "hubbsly-work",
          "ProductKey": "hubbsly",
          "DisplayName": "Hubbsly Work",
          "Classification": "tenant",
          "Level": 2,
          "Entitlements": [ "feature:work", "work:cards:create" ],
          "Limits": {
            "Seats": 4
          },
          "DefaultSeatLimit": 4,
          "DefaultCreditGrants": [
            {
              "BucketKey": "ai-chat",
              "Amount": 500,
              "Period": "monthly"
            }
          ],
          "ProviderPrices": [
            {
              "ProviderName": "stripe",
              "PriceId": "price_123",
              "Currency": "USD",
              "BillingPeriod": "monthly"
            }
          ]
        }
      ]
    }
  }
}
```

## Service Operations, Auditing, And Permissions

This core package only defines contracts. Runtime service operations are tagged in `IBeam.Licensing.Services` so IBeam service policies and audit logging can protect calls such as license grants, updates, revocations, and seat assignment changes.

## Data Storage

This package does not create tables or data stores.

| Storage Item | Created By This Package | Notes |
|---|---:|---|
| License plan data | No | Plans come from configuration by default. |
| Tenant license data | No | Persist through an `ILicensingStore` implementation. |
| Seat assignment data | No | Persist through an `ILicensingStore` implementation. |

## Package Relationships

| Package | Role |
|---|---|
| `IBeam.Licensing` | Core contracts and models. |
| `IBeam.Licensing.Services` | Service-layer orchestration and entitlement checks. |
| `IBeam.Licensing.Api` | Optional ASP.NET Core endpoints. |

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents should keep licensing checks in services. Controllers should call services and translate responses only.

## Version Notes

- Targets `net10.0`.
- Designed to work with IBeam Identity or bring-your-own authentication.
- Package version is assigned by the repository release workflow.
