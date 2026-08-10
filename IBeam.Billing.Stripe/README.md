# IBeam.Billing.Stripe

Optional Stripe Checkout and verified-webhook adapter for `IBeam.Billing`.

```csharp
builder.Services.AddIBeamStripeBilling(builder.Configuration);
```

Load secrets through host configuration, preferably a managed secret store:

```json
{
  "IBeam": {
    "Billing": {
      "Stripe": {
        "SecretKey": "load-from-secret-store",
        "WebhookSecret": "load-from-secret-store"
      }
    }
  }
}
```

The adapter never logs either secret. Checkout quantity is the total licensed seat count, including
the three-seat minimum when that offer requires three. Stripe metadata carries the IBeam purchase
and offer references used by the provider-neutral webhook processor.
