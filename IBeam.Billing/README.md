# IBeam.Billing

`IBeam.Billing` contains provider-neutral billing contracts and commercial-state models for IBeam-backed applications.

Install this package when shared code needs to describe customers, subscriptions, invoices, prices, payment method references, and billing-provider events without taking a dependency on a payment provider or licensing service.

```powershell
dotnet add package IBeam.Billing
```

## Billing Versus Licensing

Billing answers commercial questions:

- Who is the billing customer?
- What subscription, invoice, contract, marketplace purchase, or support-managed arrangement exists?
- Which provider customer, price, payment method, invoice, or event does this record map to?
- What happened commercially, and what should a reconciler do next?

Licensing answers runtime grant questions:

- Which tenant has the durable product grant?
- Which entitlements are available?
- Which seats are assigned?
- Is the grant currently eligible for runtime use?

Billing should not directly authorize runtime API calls. A reconciler can translate billing events into licensing grants, renewals, suspensions, revocations, seat quantities, and credit grants. Runtime services should continue to enforce access through Licensing and Credits.

## Billing Modes

Use `BillingModes` to represent the commercial model without hard-coding a provider:

| Mode | Meaning |
|---|---|
| `self-service-monthly` | A user or tenant buys and renews through a checkout/customer portal flow. |
| `annual-contract` | A contracted tenant account with annual or custom terms. |
| `manual-invoice` | A tenant is invoiced manually or through an accounting process. |
| `marketplace` | Billing is managed by an external marketplace. |
| `support-managed` | Support or operations manually controls the commercial record. |

## Core Models

| Area | Type(s) | Purpose |
|---|---|---|
| Customers | `BillingCustomerInfo`, `CreateBillingCustomerRequest` | Tenant-owned billing profile with optional buying user and provider customer reference. |
| Subscriptions | `BillingSubscriptionInfo`, `CreateBillingSubscriptionRequest` | Commercial subscription or contract state, optional plan key, seats, provider subscription, and price reference. |
| Invoices | `BillingInvoiceInfo`, `CreateBillingInvoiceRequest` | Invoice state, amounts, due/paid dates, hosted invoice reference, and provider invoice reference. |
| Payment methods | `BillingPaymentMethodReferenceInfo` | Safe payment method pointer such as provider id, card brand, and last four digits. |
| Prices | `BillingPriceReferenceInfo` | Provider price reference with product/plan keys but no dependency on Licensing. |
| Provider events | `BillingProviderEventInfo`, `RecordBillingProviderEventRequest` | Provider webhook or marketplace event metadata with a provider/id based idempotency key. |
| Contracts | `IBillingCustomerService`, `IBillingSubscriptionService`, `IBillingInvoiceService`, `IBillingProviderEventService`, `IBillingStore` | Service and persistence boundaries for future implementations. |

## Tenant And User Ownership

Every durable billing record is tenant-scoped. Records may also carry an optional `UserId` for single-user purchases, buyer attribution, or account owner display. Enterprise and marketplace flows can leave `UserId` empty while still linking records to the tenant.

## Provider Boundaries

Provider-specific payloads should not live in these public models. Store raw webhook bodies in an app-controlled blob/table and put the safe pointer in `PayloadReference`. Keep provider secrets and signing keys outside the repository and outside billing records.

## Package Relationships

| Package | Role |
|---|---|
| `IBeam.Billing` | Core commercial contracts and models. |
| `IBeam.Billing.Services` | Future service-layer orchestration and in-memory store. |
| `IBeam.Billing.Api` | Future optional ASP.NET Core endpoints. |
| `IBeam.Licensing` | Runtime license grants and entitlements, independent of Billing. |
