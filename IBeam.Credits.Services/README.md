# IBeam.Credits.Services

`IBeam.Credits.Services` provides reservation, settlement, release, expiration, and usage recording services for generic IBeam credits.

```powershell
dotnet add package IBeam.Credits.Services
```

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Reservations | `CreditReservationService` | Reserves max credits before work, then settles actual usage or releases the hold. |
| Usage recording | `ICreditUsageRecorder` | Records direct fail-open or trusted usage as debit ledger entries. |
| Store | `InMemoryCreditStore` | Default local/dev/test ledger and reservation persistence. |
| DI | `AddIBeamCreditServices(...)` | Registers the credit service stack and IBeam service-operation support. |

## Reservation Flow

```csharp
var reservation = await reservations.ReserveAsync(tenantId, new ReserveCreditsRequest
{
    CreditAccountId = accountId,
    BucketKey = "ai-chat",
    EstimatedAmount = 10,
    MaxAmount = 50,
    IdempotencyKey = requestId
});

try
{
    var actual = await RunProviderAsync();
    await reservations.SettleAsync(tenantId, reservation.CreditReservationId, new SettleCreditReservationRequest
    {
        ActualAmount = actual
    });
}
catch
{
    await reservations.ReleaseAsync(tenantId, reservation.CreditReservationId, new ReleaseCreditReservationRequest
    {
        Reason = "operation-failed"
    });
    throw;
}
```

Settlement writes the actual debit to the append-only ledger. Releasing or expiring a reservation returns the reserved amount to availability because no debit is written.
