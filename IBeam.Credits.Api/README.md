# IBeam.Credits.Api

`IBeam.Credits.Api` provides optional ASP.NET Core controller wiring for credit balance summaries, ledger reads, and reservation reads.

```csharp
using IBeam.Credits.Api;

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddIBeamCreditsApi(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

## Endpoint Overview

```http
POST /api/credits/tenants/{tenantId}/runtime-summary
POST /api/credits/tenants/{tenantId}/bootstrap
GET  /api/credits/tenants/{tenantId}/admin/ledger?creditAccountId={creditAccountId}&bucketKey={bucketKey}
GET  /api/credits/tenants/{tenantId}/admin/reservations?creditAccountId={creditAccountId}&bucketKey={bucketKey}
```

Runtime summaries are UI guidance only. Host applications must enforce credit spending server-side through `ICreditPolicyService`, `ICreditReservationService`, or the license-credit gate integration.

Admin endpoints require authentication by default; production hosts should add tenant admin policies or API credential scopes before exposing ledger and reservation data.
