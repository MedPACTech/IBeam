# IBeam.Licensing.Services

`IBeam.Licensing.Services` provides the service-layer implementation for IBeam tenant licensing: plan lookup, tenant license grants, license updates/revocation, seat assignment, and entitlement authorization.

```powershell
dotnet add package IBeam.Licensing.Services
```

For ASP.NET Core endpoints, use `IBeam.Licensing.Api`.

## When To Use This

- You want runtime services for tenant license administration.
- You need `ILicenseAuthorizer` to guard product features.
- You want service-operation tags and audit support around licensing mutations.
- You want an in-memory licensing store for tests, local development, or simple prototypes.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Plan catalog | `ConfigurationLicensePlanCatalogProvider` | Reads plan definitions from `IBeam:Licensing`. |
| Tenant licenses | `TenantLicenseService` | Grants, updates, lists, and revokes tenant licenses. |
| Seat assignments | `LicenseSeatAssignmentService` | Assigns and revokes seats for users, credentials, agents, or external subjects. |
| Entitlement checks | `LicenseAuthorizer` | Checks tenant license status, entitlements, and seat requirements. |
| Store | in-memory `ILicensingStore` implementation | Default development/test persistence. |
| DI | `AddIBeamLicensingServices(...)` | Registers the licensing service stack. |

## Architecture Fit

```text
API <-- DTO/model object --> Service <-- Entity --> Repository
```

This package is the service layer. Licensing business rules live here. APIs should call these services, and repository implementations should persist state without owning authorization decisions.

## Quick Start

```csharp
using IBeam.Licensing.Services;

builder.Services.AddIBeamLicensingServices(builder.Configuration);
```

Configure plans:

```json
{
  "IBeam": {
    "Licensing": {
      "Plans": [
        {
          "Key": "hubbsly-work",
          "DisplayName": "Hubbsly Work",
          "Description": "Work module access for users and agents.",
          "Entitlements": [ "feature:work", "work:cards:create", "mcp:tools" ],
          "Limits": {
            "Seats": 4,
            "McpCallsPerMonth": 10000
          },
          "Metadata": {
            "product": "hubbsly",
            "module": "work"
          }
        }
      ]
    }
  }
}
```

Grant a tenant license:

```csharp
var license = await tenantLicenses.GrantLicenseAsync(
    tenantId,
    new GrantTenantLicenseRequest
    {
        PlanKey = "hubbsly-work",
        ProviderName = "stripe",
        ProviderCustomerId = "cus_123",
        ProviderSubscriptionId = "sub_123"
    },
    createdByUserId,
    ct);
```

Assign a seat:

```csharp
await seatAssignments.AssignSeatAsync(
    tenantId,
    license.LicenseId,
    new AssignLicenseSeatRequest
    {
        Subject = new LicenseSubject(LicenseSubjectTypes.Agent, "codex")
    },
    createdByUserId,
    ct);
```

Check access before a feature runs:

```csharp
await licenseAuthorizer.RequireEntitlementAsync(
    tenantId,
    new LicenseSubject(LicenseSubjectTypes.User, userId.ToString()),
    "work:cards:create",
    ct);
```

## Service Operations

Licensing mutation methods are tagged with IBeam operation names. These names are used by service policies, audit logging, and future permission tooling.

| Service | Class Operation | Representative Method Operations |
|---|---|---|
| Tenant licenses | `licensing.licenses` | `licensing.licenses.list`, `licensing.licenses.grant`, `licensing.licenses.update`, `licensing.licenses.revoke` |
| Seat assignments | `licensing.seats` | `licensing.seats.list`, `licensing.seats.assign`, `licensing.seats.revoke` |

Example policy:

```json
{
  "IBeam": {
    "Services": {
      "Authorization": {
        "Operations": {
          "licensing.licenses.grant": {
            "AllowedRoles": [ "Owner", "Admin" ]
          },
          "licensing.seats.revoke": {
            "AllowedRoles": [ "Owner", "Admin" ]
          }
        }
      }
    }
  }
}
```

## Auditing

When IBeam service auditing is enabled, license grants, updates, revocations, and seat mutations should be captured at the service boundary. The audit record should describe the database-facing entity state before and after mutation, not an API DTO decorated with external data.

## Data Storage

The bundled store is in-memory and intended for local development, tests, and simple hosts. Production applications should replace `ILicensingStore` with an Azure Table, EF, SQL, or application-specific provider.

```csharp
builder.Services.AddIBeamLicensingServices(builder.Configuration);
builder.Services.AddScoped<ILicensingStore, MyLicensingStore>();
```

Register the replacement after `AddIBeamLicensingServices` so the host application's store wins.

## Extension Points

| Extension Point | Interface | Why Replace It |
|---|---|---|
| License persistence | `ILicensingStore` | Store licenses and seats in the host data platform. |
| Plan catalog | `ILicensePlanCatalogProvider` | Load plans from database, billing provider, or another config source. |
| Entitlement checks | `ILicenseAuthorizer` | Add product-specific quota or billing-state rules. |
| Provider hooks | `ILicenseExtension` | Integrate Stripe, Azure Marketplace, manual grants, or custom billing. |

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents should tag new custom licensing methods with `[IBeamOperation("licensing.<area>.<action>")]` and route work through `IServiceOperationExecutor` when policy/audit behavior is required.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Entitlement check fails | Tenant has no active license with the entitlement | Verify plan configuration and tenant license status. |
| Seat check fails | Subject does not have an active assignment | Assign a seat or use a plan/rule that does not require one. |
| Data disappears after restart | In-memory store is active | Register a persistent `ILicensingStore`. |
| Audits are missing | Service auditing not configured | Register IBeam service auditing and a sink/logger package. |

## Version Notes

- Targets `net10.0`.
- Uses IBeam service-operation patterns.
- Package version is assigned by the repository release workflow.
