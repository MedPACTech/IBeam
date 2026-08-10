# IBeam.Billing.Licensing

`IBeam.Billing.Licensing` is the optional bridge from commercial billing state to durable IBeam license grants.

```powershell
dotnet add package IBeam.Billing.Licensing
```

## Why This Is Separate

`IBeam.Billing` records payment and commercial state. `IBeam.Licensing` authorizes runtime application use. This package lets a host opt into translating billing subscriptions, provider prices, cancellations, payment failures, and manual contracts into license grants without making either core package depend on the other.

## Quick Start

```csharp
builder.Services.AddIBeamBillingLicenseReconciliation(options =>
{
    options.PriceMappings.Add(new BillingPricePlanMappingOptions
    {
        ProviderName = "stripe",
        PriceId = "price_pro_monthly",
        PlanKey = "hubbsly-pro"
    });
});
```

Then reconcile a billing subscription after checkout, invoice payment, webhook ingestion, or manual account setup:

```csharp
var result = await reconciler.ReconcileAsync(
    tenantId,
    new ReconcileBillingLicenseRequest
    {
        Subscription = subscription,
        EventType = "invoice.paid"
    },
    ct);
```

## Behavior

- Verified paid purchases automatically receive one stable GUID license key when this bridge is registered.
- Before Identity onboarding, the fulfilled purchase is the durable unclaimed grant and retains its plan and total seat limit.
- One-seat and multi-seat purchases both create one license key; an expansion purchase can explicitly retain an existing key.
- Provider customer and subscription references remain replaceable billing bindings and never become license identity.
- `IBillingPurchaseClaimService` issues a short-lived one-time claim token after fulfillment; only its SHA-256 hash is stored.
- After Identity verifies the signed-in user's email, the host supplies that user id, verified email, and selected tenant to `ClaimAsync`.
- A successful claim materializes the pre-created GUID key as the tenant license and assigns the buyer the first seat.
- Same-user retries are idempotent; expired, wrong-buyer, reused, and cross-tenant claims are rejected.
- Payment success creates or renews a tenant license.
- Manual invoice, annual contract, and support-managed subscriptions can use the same reconciler.
- Payment failure can suspend, expire, or ignore the matching license.
- Cancellation can suspend, expire, revoke now, or schedule revocation through metadata.
- Price mappings can come from configuration or be inferred from subscription `PlanKey` or price `PlanKey`.
