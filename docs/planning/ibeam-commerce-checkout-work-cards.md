# IBeam Commerce Checkout And Fulfillment Work Cards

These cards extend the completed IBeam Billing, Licensing, Credits, and persistence foundation with a provider-neutral public purchase flow.

## Decisions

- A purchase creates or updates one durable license, never one license per seat.
- An individual license starts with one total seat and can expand later.
- A multi-user plan starts with three total seats. Provider quantity means total seats, not three base seats plus extras.
- Checkout may complete before an IBeam Identity user or tenant exists.
- A successful provider webhook is authoritative for fulfillment; the browser return redirect is not proof of payment.
- Internal license keys and correlation identifiers use GUIDs. One-time claim secrets are separate, expire, and are stored hashed.
- Billing stays provider-neutral. Stripe, PayPal, or another processor plugs in through adapter contracts and provider bindings.

## Delivery Order

```text
COM 001 -> COM 002 -> COM 003 -> COM 004 -> COM 005 -> COM 006 -> COM 007
COM 008 -> COM 009 -> COM 010 -> COM 011 -> COM 012 -> COM 013 -> COM 014
```

## Cards

### IBeam COM 001: Define Commerce Offers And Total-Seat Pricing

Add provider-neutral offer and pricing models that map a public plan selection to product, plan, billing interval, currency, and total seat quantity.

Acceptance criteria:

- An individual offer has one license and one total seat by default.
- A multi-user offer has one license and a minimum of three total seats.
- Provider quantity is explicitly interpreted as total seats, not additional seats.
- Pricing supports a base amount, per-seat amount, tiers, and provider price references without provider SDK dependencies.
- Validation and tests cover individual purchase, three-seat purchase, expansion, invalid quantities, and price normalization.

### IBeam COM 002: Add Stable GUID License Keys And Lookup

Expose a stable GUID-backed license key for support, claiming, lookup, and cross-provider correlation without exposing storage partition details.

Acceptance criteria:

- Every new license has a non-empty immutable GUID license key.
- Existing license records receive a backward-compatible key or migration strategy.
- Licensing services can resolve a license by key with tenant authorization where required.
- Public responses reveal only the safe key; provider ids and storage keys remain private.
- Persistence and tests cover uniqueness, lookup, migration compatibility, and tenant isolation.

### IBeam COM 003: Add Provider-Neutral Checkout Gateway Contracts

Define checkout, customer-portal, webhook-verification, and provider-binding contracts so host applications can select a payment processor through dependency injection and configuration.

Acceptance criteria:

- Contracts cover creating checkout sessions, retrieving provider status, creating portal sessions, and verifying webhook payloads.
- Requests use IBeam offer keys, total seats, correlation ids, success URLs, and cancel URLs.
- Results return provider-neutral status plus opaque provider references.
- Core Billing projects do not reference Stripe, PayPal, or another provider SDK.
- Contract tests use two fake providers to prove provider selection is replaceable.

### IBeam COM 004: Model Purchases Before Identity Or Tenant Exists

Add a pending purchase aggregate that can safely record a public buyer and paid offer before an IBeam user or tenant has been created.

Acceptance criteria:

- Pending purchases have GUID ids and may omit tenant id and user id.
- Records capture normalized email, offer, total seats, provider binding, amounts, currency, status, and timestamps.
- The model distinguishes initiated, awaiting payment, paid, fulfilled, claimed, expired, canceled, refunded, and failed states.
- Duplicate provider events cannot create duplicate pending purchases or licenses.
- PII retention, redaction, expiration, and tests are documented.

### IBeam COM 005: Add Public Checkout And Purchase-Status API

Expose anonymous-safe endpoints that start checkout and report limited purchase status without requiring an existing IBeam Identity account.

Acceptance criteria:

- `POST /api/commerce/checkout-sessions` validates offer and total seats, creates a pending purchase, and returns a hosted checkout URL.
- `GET /api/commerce/purchases/{purchaseId}/status` returns only safe status and next-action data using a protected correlation token.
- Success and cancel URLs are allow-listed; clients cannot submit arbitrary redirect targets.
- Rate limiting, idempotency keys, anti-enumeration behavior, and structured errors are included.
- Tests cover highest-plan checkout with three total seats and no tenant or user.

### IBeam COM 006: Normalize Verified Provider Webhooks

Create an orchestration boundary that verifies provider signatures, records events idempotently, and maps processor-specific payloads into normalized commerce events.

Acceptance criteria:

- Unverified webhook payloads never mutate Billing or Licensing state.
- Normalized events cover checkout completion, payment success/failure, renewal, seat quantity change, cancellation, refund, and dispute.
- Processing is idempotent by provider and provider event id.
- Browser redirects do not fulfill purchases; verified events or an explicit trusted reconciliation path do.
- Tests cover replay, out-of-order delivery, invalid signatures, transient retries, and unsupported event types.

### IBeam COM 007: Add Optional Stripe Billing Adapter

Implement Stripe as the first optional checkout and webhook adapter without introducing Stripe dependencies into provider-neutral IBeam packages.

Acceptance criteria:

- A separate adapter package implements the checkout gateway and webhook contracts.
- Checkout metadata carries IBeam purchase id, offer key, and total seat quantity.
- Stripe subscription quantity maps to total seats, including the three-seat minimum.
- API keys and webhook secrets come from host secret configuration and are never logged.
- Adapter tests cover session creation, verified completion, renewal, seat change, cancellation, and replay.

### IBeam COM 008: Fulfill Paid Purchases Into One License

Turn a normalized paid purchase into one durable license with the selected plan and total seat limit, whether or not the purchase has been claimed by a tenant.

Acceptance criteria:

- Fulfillment creates exactly one license for a purchase, regardless of seat count.
- Individual purchases produce one license with one seat; multi-user purchases produce one license with at least three total seats.
- Repeated fulfillment returns the same license and never duplicates grants.
- Provider references are stored as replaceable bindings rather than license identity.
- Tests cover initial purchase, replay, three-seat purchase, and later expansion.

### IBeam COM 009: Add Secure Purchase Claim And Onboarding Flow

Allow a buyer returning from checkout to sign up or sign in, create or select a tenant, and claim the paid license securely.

Acceptance criteria:

- A one-time expiring claim token is separate from the GUID license key and is stored hashed.
- Claiming links the pending purchase, billing customer, subscription, license, tenant, and buyer user atomically or recoverably.
- The buyer receives the first seat after identity proof; remaining seats stay available for assignment.
- Claims are idempotent and reject reuse, wrong buyer, expired token, and cross-tenant takeover.
- Tests cover new user/new tenant, existing user, retry, expiration, and three-seat onboarding.

### IBeam COM 010: Reconcile Renewals, Seat Changes, And Cancellations

Extend lifecycle reconciliation so provider subscription changes update the existing license instead of creating replacements.

Acceptance criteria:

- Renewal extends the same license and preserves its GUID key and assignments.
- Seat increases update the total seat limit and keep existing assignments.
- Seat decreases below assigned seats enter a deterministic over-assigned state and policy flow rather than deleting users silently.
- Individual licenses can expand beyond one seat; multi-user plans enforce their configured minimum of three.
- Cancellation, failed payment, refund, and grace behavior are covered by policy and tests.

### IBeam COM 011: Support Payment-Provider Migration

Allow a tenant subscription to move from Stripe to PayPal or another processor while retaining the same internal purchase history and license.

Acceptance criteria:

- Internal billing, purchase, subscription, and license ids remain provider-neutral.
- A license can retain historical provider bindings and one active binding without exposing provider ids as its identity.
- Migration can activate the new provider before retiring the old binding without double granting access.
- Event idempotency is scoped by provider, and reconciliation prevents duplicate renewals across providers.
- Tests use Stripe-like and PayPal-like fake adapters to prove the switch preserves the license and seats.

### IBeam COM 012: Persist Checkout, Purchase, Claim, And Provider Bindings

Extend the durable commerce provider with pending purchases, checkout attempts, claim records, and provider-binding indexes.

Acceptance criteria:

- Azure Table persistence supports lookup by GUID purchase/license key, provider event id, provider customer, and provider subscription.
- State transitions use optimistic concurrency and safe retry behavior.
- Claim and fulfillment idempotency survive process restarts.
- Sensitive tokens are hashed and provider payload bodies are not stored in public records.
- Tests cover persistence, concurrency, replay, index lookup, and cleanup of expired pending purchases.

### IBeam COM 013: Add Commerce Administration And Recovery APIs

Provide protected endpoints for support and tenant administrators to inspect purchase/license state and recover failed fulfillment safely.

Acceptance criteria:

- Authorized admins can inspect pending purchase, billing, license, seat, claim, and provider-binding status.
- Support can retry verified event processing or fulfillment without resubmitting payment.
- Admins can resend onboarding/claim instructions by issuing a new one-time token and invalidating the old one.
- Manual corrections require explicit authorization, reason, audit record, and idempotency key.
- Responses redact secrets, provider payloads, and unnecessary buyer PII.

### IBeam COM 014: Add End-To-End Commerce Validation And Guide

Validate and document the complete public purchase flow and provider-portability model for IBeam consumers.

Acceptance criteria:

- An end-to-end test buys the highest plan with three total seats before Identity exists, processes a verified event, creates one license, claims it, and assigns the buyer seat.
- Tests cover an individual one-seat purchase that later expands, webhook replay, failed payment, cancellation, and provider migration.
- Documentation maps every public/admin endpoint and shows DTO examples and sequence diagrams.
- Consumer guidance clearly separates provider checkout UI, IBeam Billing, Licensing, Identity onboarding, and runtime enforcement.
- Security guidance covers redirect allow-lists, signatures, idempotency, claim-token handling, PII, secrets, and audit.
