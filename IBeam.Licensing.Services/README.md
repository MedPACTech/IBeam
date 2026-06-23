# IBeam.Licensing.Services

Tenant licensing services, plan catalog, entitlement authorization, and default in-memory store for IBeam-backed applications.

For ASP.NET Core endpoints, use `IBeam.Licensing.Api`.

Install:

```powershell
dotnet add package IBeam.Licensing.Services
```

Register the default services:

```csharp
builder.Services.AddIBeamLicensingServices(builder.Configuration);
```

Configure plans through `IBeam:Licensing`:

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
        },
        {
          "Key": "hubbsly-money",
          "DisplayName": "Hubbsly Money",
          "Entitlements": [ "feature:money", "money:close:update" ],
          "Limits": {
            "Seats": 2
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

Assign a seat to a user, API credential, or agent:

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

Check access before running a feature:

```csharp
await licenseAuthorizer.RequireEntitlementAsync(
    tenantId,
    new LicenseSubject(LicenseSubjectTypes.Agent, "codex"),
    "work:cards:create",
    ct);
```

The bundled store is in-memory and intended for local development, tests, and simple hosts. Production applications should replace `ILicensingStore` with an Azure Table, EF, or application-specific provider.

To replace the store:

```csharp
builder.Services.AddIBeamLicensingServices(builder.Configuration);
builder.Services.AddScoped<ILicensingStore, MyLicensingStore>();
```

Register the replacement after `AddIBeamLicensingServices` so the host application's store wins.
