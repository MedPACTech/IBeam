# Qurvia Commerce API And Frontend Implementation Prompt

Use this prompt to update the Qurvia API and frontend to consume **IBeam 2.9.45** commerce, licensing, Identity, seat, provider migration, and recovery capabilities. Work in the existing Qurvia repositories and follow their established .NET, frontend, routing, state-management, component, validation, authorization, testing, and deployment patterns.

Do not build a separate demo or landing page. Implement the real public purchase, onboarding, account, and administration workflows in Qurvia.

## Goal

Support this complete workflow:

1. A visitor who does not yet have a Qurvia Identity account selects a public plan.
2. An individual plan means one license with one total seat.
3. A multi-user plan means one license with a minimum of three total seats. Three is the total, not three seats plus an owner.
4. Qurvia creates a provider-neutral pending purchase and redirects to hosted checkout.
5. A verified Stripe, PayPal, or future provider webhook marks the purchase paid and fulfills it with one stable GUID license key.
6. Qurvia sends a short-lived onboarding claim link to the verified buyer email.
7. The buyer signs in or creates an IBeam Identity user, selects or creates the tenant, and claims the purchase.
8. Claiming creates or reuses the one license, assigns the buyer to the first seat, binds the purchase to the tenant/user, and completes the purchase.
9. Qurvia API operations enforce Licensing, AccessControl, and Credits server-side. The frontend only presents the effective state.
10. Administrators can inspect billing/license state, manage seats, retry safe fulfillment, rotate onboarding links, and migrate payment providers without replacing the license.

## Architecture Boundaries

Keep these concerns separate:

- **Qurvia public website/frontend** owns plan presentation, seat selection, checkout navigation, return-state UX, onboarding UX, and administration screens.
- **Qurvia API** owns provider adapters, host orchestration endpoints, Identity-session resolution, emails/notifications, CORS, authorization, and Qurvia-specific DTO composition.
- **IBeam Billing** owns offers, quotes, purchases, checkout attempts, subscriptions, provider events, payment state, and idempotency.
- **IBeam Licensing** owns durable license grants, stable GUID keys, status, entitlements, limits, and seat assignments.
- **IBeam Identity** owns users, tenants, login/OTP, roles, permissions, API credentials, and OAuth clients.
- **IBeam Credits** owns metered grants, reservations, settlement, and balances where Qurvia operations consume credits.

Billing must never directly authorize a Qurvia runtime action. A paid purchase produces Licensing state; Qurvia services then enforce that licensing state for every protected operation.

## Packages

Update all installed IBeam commerce package references to `2.9.45`. Add only packages the Qurvia project actually uses:

```xml
<PackageReference Include="IBeam.Billing" Version="2.9.45" />
<PackageReference Include="IBeam.Billing.Services" Version="2.9.45" />
<PackageReference Include="IBeam.Billing.Api" Version="2.9.45" />
<PackageReference Include="IBeam.Billing.Licensing" Version="2.9.45" />
<PackageReference Include="IBeam.Billing.Stripe" Version="2.9.45" />
<PackageReference Include="IBeam.Licensing" Version="2.9.45" />
<PackageReference Include="IBeam.Licensing.Services" Version="2.9.45" />
<PackageReference Include="IBeam.Licensing.Api" Version="2.9.45" />
<PackageReference Include="IBeam.Commerce.Repositories.AzureTable" Version="2.9.45" />
```

Retain Qurvia's existing IBeam Identity, AccessControl, Credits, and AI packages and align their versions to `2.9.45` where they are referenced.

## API Registration

Compose commerce alongside the existing Identity registration. Register in-memory defaults before replacing them with Azure Table stores:

```csharp
using IBeam.Billing.Api;
using IBeam.Billing.Licensing;
using IBeam.Commerce.Repositories.AzureTable;
using IBeam.Licensing.Api;

builder.Services.AddIBeamBillingApi(builder.Configuration);
builder.Services.AddIBeamLicensingApi(builder.Configuration);
builder.Services.AddIBeamBillingLicenseReconciliation(options =>
{
    options.MinimumMultiUserSeats = 3;
    options.PaymentFailureBehavior = BillingLicensePaymentFailureBehaviors.Grace;
    options.DefaultGracePeriodDays = 7;
    options.CancellationBehavior = BillingLicenseCancellationBehaviors.Suspend;
    options.RefundBehavior = BillingLicenseRefundBehaviors.Revoke;
    options.SeatDecreaseBehavior = BillingLicenseSeatDecreaseBehaviors.PreserveAssignments;
});
builder.Services.AddIBeamCommerceAzureTableStores(builder.Configuration);

// Register Qurvia's real adapter. Do not put provider secrets in source.
builder.Services.AddScoped<IBillingCheckoutGateway, StripeBillingCheckoutGateway>();
```

Preserve Qurvia's existing authentication and authorization middleware order. The final pipeline must include authentication, authorization, rate limiting, controllers, and the existing IBeam endpoint mappings.

## Configuration

Add environment-specific configuration without committing secrets:

```json
{
  "IBeam": {
    "Billing": {
      "PublicCheckout": {
        "DefaultProviderName": "stripe",
        "AllowedReturnOrigins": [
          "https://www.qurvia.com",
          "https://app.qurvia.com"
        ],
        "StatusTokenSigningKey": "load-from-key-vault",
        "StatusTokenLifetimeMinutes": 30,
        "PurchaseLifetimeMinutes": 60
      },
      "Licensing": {
        "MinimumMultiUserSeats": 3,
        "DefaultGracePeriodDays": 7
      }
    },
    "Commerce": {
      "AzureTables": {
        "StorageConnectionString": "load-from-configuration"
      }
    }
  }
}
```

Use Qurvia's actual configuration section names if its host already maps these settings. Store Stripe/PayPal keys, webhook secrets, status signing keys, email credentials, and storage connection strings in Key Vault, deployment secrets, or local user-secrets.

## Offer Catalog

Define Qurvia offers in API configuration or Qurvia's existing product catalog. Every offer needs:

- Stable offer key, product key, and license plan key.
- Display name and billing period.
- Currency and provider price references.
- Seat policy and provider-neutral pricing.
- Exactly one license quantity.

Rules:

- Individual: default `1`, minimum `1`, one license.
- Multi-user: default `3`, minimum `3`, one license.
- Additional seats increase `totalSeats`; they never create another license.
- The backend calculates and returns the quote. The frontend must not be trusted to calculate the charge.

If Qurvia has no public plan endpoint, add a read-only host endpoint such as `GET /api/commerce/offers` that projects safe fields from `IBillingOfferCatalogProvider`. Never expose provider secrets, internal metadata, or inactive offers.

## Built-In IBeam Endpoints

Use the controllers supplied by `IBeam.Billing.Api`:

```text
POST /api/commerce/checkout-sessions
GET  /api/commerce/purchases/{purchaseId}/status?token={statusToken}
POST /api/billing/webhooks/{providerName}

GET  /api/billing/tenants/{tenantId}/customers
GET  /api/billing/tenants/{tenantId}/subscriptions
GET  /api/billing/tenants/{tenantId}/invoices
GET  /api/billing/tenants/{tenantId}/provider-events

GET  /api/billing/commerce/tenants/{tenantId}/purchases/{purchaseId}
POST /api/billing/commerce/tenants/{tenantId}/provider-events/{providerName}/{providerEventId}/retry
POST /api/billing/commerce/tenants/{tenantId}/purchases/{purchaseId}/retry-fulfillment
POST /api/billing/commerce/tenants/{tenantId}/purchases/{purchaseId}/resend-claim
POST /api/billing/commerce/tenants/{tenantId}/purchases/{purchaseId}/corrections
```

The public status response is intentionally redacted. Admin recovery endpoints require a tenant claim matching the route and an Owner/Administrator/Admin role or `billing.commerce.admin` permission.

## Host-Owned API Endpoints

Implement these in Qurvia because they combine IBeam services with Qurvia Identity, notification, and provider behavior.

### Claim a purchase

```http
POST /api/commerce/purchases/{purchaseId}/claim
Authorization: Bearer {human-user-token}
```

Request:

```json
{
  "claimToken": "one-time-token"
}
```

Resolve tenant id, user id, and verified email from the authenticated IBeam Identity session. Do not accept those values from the request body.

```csharp
var result = await claims.ClaimAsync(new ClaimBillingPurchaseLicenseRequest
{
    ClaimToken = request.ClaimToken,
    TenantId = currentIdentity.TenantId,
    UserId = currentIdentity.UserId,
    VerifiedEmail = currentIdentity.VerifiedEmail
}, ct);
```

Return a safe projection containing purchase id, plan, license key, seat limit, assignment count, and completion status. Do not return token hashes or buyer email hashes.

### Deliver onboarding

After a verified paid webhook fulfills the purchase, call `IBillingPurchaseClaimService.IssueAsync(purchaseId)` from an idempotent Qurvia workflow and send the raw token once to the normalized buyer email. Prefer a link into Qurvia's onboarding page. Never place the raw token in logs, provider metadata, analytics, or the public purchase-status response.

### Migrate providers

Create a tenant-admin endpoint such as:

```http
POST /api/billing/tenants/{tenantId}/subscriptions/{subscriptionId}/migrate-provider
```

The API must first create and verify an active replacement subscription with the target provider. Then call `IBillingProviderMigrationService.MigrateAsync`. Preserve the internal subscription id, license GUID, seat assignments, and binding history. Only one provider binding may be active. Do not cancel the source provider until target setup and IBeam migration both succeed.

### Customer portal and seat changes

Expose a tenant-admin endpoint that creates a short-lived provider customer-portal session through the configured gateway. Handle provider seat-change webhooks by updating the subscription and reconciling Licensing. Expansion from one seat into multi-user must become three total seats. Seat reductions must follow the configured policy and must never silently remove assigned users.

## Webhook Requirements

For each provider adapter:

1. Read the raw request body once.
2. Verify its signature and timestamp before mutating any IBeam state.
3. Normalize the provider event to `BillingVerifiedWebhookInfo`.
4. Include the IBeam `purchaseId` in verified metadata.
5. Use the provider's immutable event id.
6. Let `IBillingWebhookProcessor` apply idempotency and lifecycle rules.
7. Return a successful provider acknowledgement for processed replays.
8. Record failed processing for controlled admin retry; never ask the provider or buyer to pay again merely to rerun fulfillment.

Cover paid, failed, canceled, refunded/disputed, renewal, and seat-change events. A failed or canceled pre-fulfillment purchase must not create a claimable license.

## Frontend Work

Implement these experiences using Qurvia's existing design system. Keep the UI operational, compact, responsive, accessible, and consistent with the application.

### Public plans and checkout

- Show active plans from the API, including billing interval, price, and included total seats.
- For individual plans, show one total seat.
- For multi-user plans, initialize the seat control at three and prevent values below three.
- Clearly show `1 license / N total seats` in the order summary.
- Submit one stable idempotency key per checkout attempt and reuse it when retrying the same request.
- Disable repeat submission while checkout creation is pending.
- Redirect only to the `checkoutUrl` returned by the API.

### Purchase return

- Read `purchaseId` and the short-lived status token from Qurvia-controlled return state.
- Poll the redacted status endpoint with bounded backoff.
- Render `await-payment`, `create-account`, `complete`, `restart-checkout`, and `contact-support` states.
- Never treat a successful browser redirect as payment confirmation.
- Stop polling when the status token expires and provide a safe restart/recovery action.

### Identity onboarding and claim

- Require OTP/password login or account creation through existing IBeam Identity UI.
- Handle tenant creation or selection using Qurvia's established onboarding flow.
- Submit only the one-time claim token to the host claim endpoint.
- On success, refresh current tenant/license context and enter Qurvia.
- Treat a same-user replay as success.
- Show a neutral error for expired, wrong-email, already-used-by-another-user, or invalid tokens without revealing whether another account exists.

### Billing administration

Add or extend an admin billing area with focused views rather than a large dashboard of decorative cards:

- **Overview**: current plan, commercial status, renewal/grace date, provider, total seats, assigned seats, available seats.
- **Seats**: searchable assignment table, add/remove actions, capacity warning, and upgrade action.
- **Invoices and subscription**: provider-safe invoice/subscription data and customer-portal action.
- **Provider connection**: current active provider binding, migration action, and read-only binding history.
- **Recovery**: purchase/event lookup, redacted state, retry fulfillment/event, rotate onboarding link, and manual correction for authorized support users.

Require a reason for recovery actions and an idempotency key for manual corrections. Confirm consequential actions. Never display claim hashes, provider payload references, full buyer PII, API keys, OAuth secrets, webhook secrets, or access tokens.

### Runtime access UX

Use IBeam runtime context to disable or hide unavailable features and explain seat/license status, but continue enforcing every protected action in the Qurvia API. Refresh context after claim, seat change, plan change, provider migration, payment recovery, and tenant switch.

## CORS And Redirects

- Allow only Qurvia's exact public/app origins in CORS and `AllowedReturnOrigins`.
- Permit required methods and headers, including Authorization and content type.
- Do not use wildcard origins with credentials.
- Keep success/cancel URLs on Qurvia-owned HTTPS origins.
- Validate any post-login return path as an application-relative allow-listed path to prevent open redirects.

## Security And Audit

- Verify raw webhook signatures before any Billing, Licensing, Identity, email, or audit mutation.
- Use distinct idempotency scopes for checkout, provider events, notification delivery, migration, and support corrections.
- Store purchase/claim/provider state in durable Azure Tables for production.
- Store claim tokens and buyer emails only as required by the framework's hashing/redaction policies.
- Resolve all tenant/user ownership from authenticated server context.
- Audit actor, tenant, action, reason, correlation id, and success/failure for recovery, migration, seat changes, and claim rotation.
- Do not log request bodies containing secrets or one-time tokens.
- Keep Billing, Licensing, Identity, and runtime authorization decisions server-side.

## Tests

Add focused API and frontend tests using Qurvia's current test stacks.

API tests must cover:

- Highest-plan anonymous checkout with three total seats and no Identity user.
- Verified paid webhook creates exactly one stable license key.
- Provider-event replay does not duplicate purchase, event, license, or email delivery.
- Buyer account creation/OTP and claim assigns the first seat and completes the purchase.
- Claim replay for the same user is idempotent; cross-tenant/user/email claim is rejected.
- Individual one-seat expansion becomes one license with three total seats.
- Failed payment/cancellation before fulfillment creates no license.
- Renewal failure/grace, cancellation, refund, and seat reduction policies.
- Stripe-to-PayPal migration preserves the license and assignments with one active binding.
- Admin authorization, tenant isolation, redaction, reason requirements, and correction idempotency.

Frontend tests must cover:

- Seat-control minimums and total-price rendering.
- Checkout request/idempotency mapping and pending-submit behavior.
- Return-page polling and all next-action states.
- Login/onboarding/claim success, expiry, and neutral errors.
- Admin role/permission visibility and protected recovery actions.
- License/seat context refresh after all commercial changes.

## Definition Of Done

- Qurvia API and frontend consume IBeam `2.9.45` packages.
- Public visitors can purchase before an Identity user exists.
- A three-seat purchase creates one license with three total seats.
- Verified payment, onboarding, Identity claim, and first-seat assignment work end to end.
- Provider webhooks and all retries are idempotent.
- Billing administrators can inspect and recover state without exposing secrets or unnecessary PII.
- Provider migration preserves the internal license and seats.
- Qurvia API enforces Licensing/AccessControl/Credits server-side.
- API/frontend tests pass, production secrets remain external, and operational setup is documented.
