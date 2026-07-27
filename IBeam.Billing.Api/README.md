# IBeam.Billing.Api

`IBeam.Billing.Api` provides optional ASP.NET Core controller wiring for IBeam billing administration and provider-event ingestion.

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
app.MapControllers();
```

## Endpoint Overview

```http
GET  /api/billing/tenants/{tenantId}/customers
GET  /api/billing/tenants/{tenantId}/subscriptions
GET  /api/billing/tenants/{tenantId}/invoices
GET  /api/billing/tenants/{tenantId}/provider-events
POST /api/billing/provider-events
```

The read endpoints are intended for admin/internal tools. The provider-event endpoint records safe provider event metadata and remains idempotent through `IBillingProviderEventService`.

## Security

Controllers require authentication, but host applications still own authorization policy and provider webhook validation. Production APIs should require tenant admin roles or API credential scopes for billing reads, and should validate provider signatures before accepting webhook events.

Billing APIs do not authorize runtime application access. Runtime services should enforce access through Licensing and Credits.
