# IBeam.Billing.Api

`IBeam.Billing.Api` provides optional ASP.NET Core controller wiring for public checkout, billing administration, and provider-event ingestion.

```powershell
dotnet add package IBeam.Billing.Api
```

## Quick Start

```csharp
using IBeam.Billing.Api;

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddIBeamBillingApi(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
```

## Endpoint Overview

```http
POST /api/commerce/checkout-sessions
GET  /api/commerce/purchases/{purchaseId}/status?token={statusToken}
POST /api/billing/webhooks/{providerName}
GET  /api/billing/tenants/{tenantId}/customers
GET  /api/billing/tenants/{tenantId}/subscriptions
GET  /api/billing/tenants/{tenantId}/invoices
GET  /api/billing/tenants/{tenantId}/provider-events
POST /api/billing/provider-events
GET  /api/billing/commerce/tenants/{tenantId}/purchases/{purchaseId}
POST /api/billing/commerce/tenants/{tenantId}/provider-events/{providerName}/{providerEventId}/retry
POST /api/billing/commerce/tenants/{tenantId}/purchases/{purchaseId}/retry-fulfillment
POST /api/billing/commerce/tenants/{tenantId}/purchases/{purchaseId}/resend-claim
POST /api/billing/commerce/tenants/{tenantId}/purchases/{purchaseId}/corrections
```

Configure public checkout with an adapter-owned signing secret and explicit return origins:

```json
{
  "IBeam": {
    "Billing": {
      "PublicCheckout": {
        "DefaultProviderName": "stripe",
        "AllowedReturnOrigins": [ "https://app.example.com" ],
        "StatusTokenSigningKey": "load-at-least-32-secret-characters-from-key-vault"
      }
    }
  }
}
```

The public endpoints are anonymous but rate limited. Checkout creation requires an idempotency key, validates the configured offer and total seats, and rejects return URLs outside the allow-list. Status responses require a short-lived signed token and omit buyer email and provider references.

Provider webhooks are anonymous because processor callbacks cannot sign in to the consuming app. The registered provider gateway verifies the raw body and signature headers before Billing records or mutates anything. Gateways map payloads to `BillingCommerceEventTypes`; processed and intentionally ignored events are idempotent by provider plus event id, while failed processing can be retried safely.

The read endpoints are intended for admin/internal tools. The provider-event endpoint records safe provider event metadata and remains idempotent through `IBillingProviderEventService`.

## Security

Commerce recovery endpoints require an authenticated principal whose tenant claim matches the route and who has the `Owner`, `Administrator`, or `Admin` role or the `billing.commerce.admin` permission. Register `AddIBeamBillingLicenseReconciliation` in the host to provide the commerce administration service. Recovery and correction requests require a support reason, corrections also require an idempotency key, and the operation executor writes the configured audit trail. Inspection responses redact buyer email and omit claim hashes and provider payload references.

Public checkout uses signed status tokens and explicit return-origin allow-lists; webhook authenticity is delegated to the selected provider gateway before state changes.

Billing APIs do not authorize runtime application access. Runtime services should enforce access through Licensing and Credits.

See [the commerce integration guide](https://github.com/MedPACTech/IBeam/blob/development/docs/commerce-integration-guide.md) for the public purchase, Identity onboarding, seat, recovery, and provider migration sequence.
