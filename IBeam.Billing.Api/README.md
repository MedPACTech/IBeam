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
GET  /api/billing/tenants/{tenantId}/customers
GET  /api/billing/tenants/{tenantId}/subscriptions
GET  /api/billing/tenants/{tenantId}/invoices
GET  /api/billing/tenants/{tenantId}/provider-events
POST /api/billing/provider-events
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

The read endpoints are intended for admin/internal tools. The provider-event endpoint records safe provider event metadata and remains idempotent through `IBillingProviderEventService`.

## Security

Controllers require authentication, but host applications still own authorization policy and provider webhook validation. Production APIs should require tenant admin roles or API credential scopes for billing reads, and should validate provider signatures before accepting webhook events.

Billing APIs do not authorize runtime application access. Runtime services should enforce access through Licensing and Credits.
