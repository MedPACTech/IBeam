# IBeam.Billing.Services

`IBeam.Billing.Services` provides the service-layer implementation for IBeam billing records, provider-event ingestion, and local in-memory persistence.

```powershell
dotnet add package IBeam.Billing.Services
```

For ASP.NET Core endpoints, use the future `IBeam.Billing.Api` package.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Customers | `BillingCustomerService` | Create, update, get, and list tenant billing customers. |
| Subscriptions | `BillingSubscriptionService` | Track provider subscriptions, contract state, plan/price references, and seats. |
| Invoices | `BillingInvoiceService` | Track invoices, payment state, due dates, and safe hosted invoice references. |
| Provider events | `BillingProviderEventService` | Idempotently ingest provider or marketplace events by provider event id. |
| Store | `InMemoryBillingStore` | Default development/test persistence for customers, subscriptions, invoices, and provider events. |
| DI | `AddIBeamBillingServices(...)` | Registers the billing service stack and IBeam service-operation support. |

## Quick Start

```csharp
using IBeam.Billing.Services;

builder.Services.AddIBeamBillingServices(builder.Configuration);
```

The bundled store is in-memory and intended for local development, tests, and prototypes. Production applications should replace `IBillingStore` with Azure Table, SQL, EF, or an application-owned provider.

```csharp
builder.Services.AddIBeamBillingServices(builder.Configuration);
builder.Services.AddScoped<IBillingStore, MyBillingStore>();
```

Register the replacement after `AddIBeamBillingServices` so the host application's store wins.

## Service Operations

Billing service methods are tagged with `IBeamOperation` names for policy, audit, and service-base consumers.

| Service | Class Operation | Representative Method Operations |
|---|---|---|
| Customers | `billing.customers` | `billing.customers.list`, `billing.customers.create`, `billing.customers.update` |
| Subscriptions | `billing.subscriptions` | `billing.subscriptions.list`, `billing.subscriptions.create`, `billing.subscriptions.update` |
| Invoices | `billing.invoices` | `billing.invoices.list`, `billing.invoices.create`, `billing.invoices.update` |
| Provider events | `billing.provider-events` | `billing.provider-events.record`, `billing.provider-events.list` |

## Provider Event Idempotency

`BillingProviderEventService.RecordEventAsync` creates a stable idempotency key from `ProviderName` and `ProviderEventId`. Replaying the same provider event returns the original billing event record instead of creating a duplicate.

## Billing And Licensing Boundary

Billing services record commercial state. They do not directly authorize application access. A later reconciler can consume billing records and provider events, then update `IBeam.Licensing` grants, renewals, suspensions, revocations, seats, and credit grants.
