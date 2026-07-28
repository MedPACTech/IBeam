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

- Payment success creates or renews a tenant license.
- Manual invoice, annual contract, and support-managed subscriptions can use the same reconciler.
- Payment failure can suspend, expire, or ignore the matching license.
- Cancellation can suspend, expire, revoke now, or schedule revocation through metadata.
- Price mappings can come from configuration or be inferred from subscription `PlanKey` or price `PlanKey`.
