# Licensing, Billing, Seats, And Credits

This guide explains how IBeam applications should compose Billing, Licensing, Seats, and Credits without coupling those systems too tightly.

The short version:

- **Billing** records payment and commercial state.
- **Licensing** records durable runtime grants.
- **Seats** assign a license grant to users, API credentials, agents, or external subjects.
- **Credits** record consumption grants, reservations, settlements, usage, and balance summaries.

Billing can create or update licenses. Licensing and Credits authorize runtime work. Client-side state is only guidance; server-side services and APIs must enforce the real rules.

## Package Roles

| Need | Package |
|---|---|
| Shared license, seat, entitlement, and plan contracts | `IBeam.Licensing` |
| Runtime license grants, seat assignment, and entitlement checks | `IBeam.Licensing.Services` |
| Optional license management and runtime context endpoints | `IBeam.Licensing.Api` |
| Shared customer, subscription, invoice, payment, and provider event contracts | `IBeam.Billing` |
| Billing customer/subscription/invoice/provider-event services | `IBeam.Billing.Services` |
| Optional billing admin/provider event controllers | `IBeam.Billing.Api` |
| Reconcile billing subscriptions into license grants | `IBeam.Billing.Licensing` |
| Shared credit account, bucket, grant, ledger, reservation, and policy contracts | `IBeam.Credits` |
| Credit policy, reservation, settlement, usage, and balance services | `IBeam.Credits.Services` |
| Optional credit runtime/admin/bootstrap controllers | `IBeam.Credits.Api` |
| Require both license entitlements and credits around an operation | `IBeam.Licensing.Credits` |
| Azure Table persistence for billing, licensing, seats, credits | `IBeam.Commerce.Repositories.AzureTable` |

Each package can be used by itself. The bridge packages are convenience layers for hosts that want the systems to cooperate.

## Recommended Runtime Flow

1. Authenticate with IBeam Identity or the host application's auth provider.
2. Issue the normal auth token.
3. Call the licensing or credit bootstrap endpoint to fetch runtime guidance for the current tenant and subject.
4. Use that guidance to shape the frontend: enabled navigation, renewal prompts, contact-admin prompts, or reduced-mode experiences.
5. Enforce every write, expensive operation, and credit spend on the server.

The bootstrap response should be treated like cached UI state. It can change while the user is working because a subscription expires, a license is revoked, a seat is removed, or credits are spent elsewhere.

```http
POST /api/credits/tenants/225925cc-995e-4584-a63b-4f2cb4f38f6f/bootstrap
Content-Type: application/json

{
  "license": {
    "subject": {
      "subjectType": "user",
      "subjectId": "user-1"
    }
  },
  "credits": {
    "creditAccountId": "c015230d-6f88-48a0-891b-9b561038db98",
    "bucketKeys": [ "ai-chat", "dictation" ]
  }
}
```

The response includes a `LicenseRuntimeContextInfo` and, when requested, a `CreditRuntimeSummaryInfo`. The response also marks credit data as `GuidanceOnly`. Use it for UX, not authority.

## Service Registration

For a full application that wants auth-adjacent runtime APIs, admin APIs, reconciliation, license-credit gates, and Azure Table persistence:

```csharp
using IBeam.Billing.Api;
using IBeam.Billing.Licensing;
using IBeam.Commerce.Repositories.AzureTable;
using IBeam.Credits.Api;
using IBeam.Licensing.Api;
using IBeam.Licensing.Credits;
using IBeam.Licensing.Services;

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddIBeamLicensingApi(builder.Configuration);
builder.Services.AddIBeamBillingApi(builder.Configuration);
builder.Services.AddIBeamCreditsApi(builder.Configuration);

builder.Services.AddIBeamBillingLicenseReconciliation(options =>
{
    options.PriceMappings.Add(new BillingPricePlanMappingOptions
    {
        ProviderName = "stripe",
        PriceId = "price_pro_monthly",
        PlanKey = "hubbsly-pro"
    });
});

builder.Services.AddIBeamLicenseCreditGate();
builder.Services.AddIBeamLicensedServiceOperations();

builder.Services.AddIBeamCommerceAzureTableStores(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapIBeamLicensing("/api", "TenantAdmin");
app.MapControllers();
```

For standalone licensing only, register `AddIBeamLicensingServices(...)` and provide an `ILicensingStore`.

For standalone credits only, register `AddIBeamCreditServices(...)` and provide credit stores.

For standalone billing only, register `AddIBeamBillingServices(...)` and provide an `IBillingStore`.

## Billing Versus Licensing

Billing should answer questions like:

- Which tenant or buyer owns the customer record?
- Which provider customer, subscription, invoice, marketplace purchase, or manual contract exists?
- Which price or plan was purchased?
- Which webhook or provider event was received?
- Should the billing record be reconciled into a grant, renewal, suspension, cancellation, or credit allocation?

Licensing should answer questions like:

- Which tenant has a runtime product grant?
- Which entitlements are present?
- Which subjects have seats?
- Is the grant active, trialing, in grace, expired, suspended, or revoked?
- Should the user be allowed to execute this operation right now?

Credits should answer questions like:

- Which bucket is being consumed?
- How many credits are available after reservations?
- Should the operation reserve first, allow overage, meter after execution, or stream-settle?
- What actual usage was recorded?

## Purchase Workflows

### Solo Purchase

A single user buys a plan for one tenant. Billing records the customer/subscription. Reconciliation grants the license. Seat policy assigns the buyer as the first seat.

```csharp
var license = await seatPolicies.GrantSingleUserLicenseAsync(
    tenantId,
    new GrantSingleUserLicenseRequest
    {
        License = new GrantTenantLicenseRequest
        {
            PlanKey = "hubbsly-pro",
            ProviderName = "stripe",
            ProviderCustomerId = customer.ProviderCustomerId,
            ProviderSubscriptionId = subscription.ProviderSubscriptionId
        },
        Subject = new LicenseSubject(LicenseSubjectTypes.User, userId.ToString("D"))
    },
    createdByUserId,
    ct);
```

Frontend behavior: let the buyer continue immediately after checkout and bootstrap runtime context again.

### Enterprise Seats

A tenant buys one license with multiple seats. The license is tenant-scoped and seat assignment controls which subjects consume the grant.

```csharp
var grant = await seatPolicies.GrantTenantSeatLicenseAsync(
    tenantId,
    new GrantTenantSeatLicenseRequest
    {
        SeatLimit = 25,
        License = new GrantTenantLicenseRequest
        {
            PlanKey = "hubbsly-enterprise"
        },
        InitialSubjects =
        [
            new LicenseSubject(LicenseSubjectTypes.User, ownerUserId.ToString("D")),
            new LicenseSubject(LicenseSubjectTypes.ApiCredential, apiCredentialId)
        ]
    },
    createdByUserId,
    ct);
```

Frontend behavior: if a user authenticates but has no seat, show a contact-admin path instead of a checkout path.

### Expired License With Reduced Mode

A tenant previously had broad access. After expiration, the host wants to allow only AI Chat CRUD while blocking notes, dictation, patients, and other app operations.

Use a broad default entitlement plus a small no-friction override:

```json
{
  "IBeam": {
    "Licensing": {
      "ServiceOperations": {
        "DefaultEntitlement": "app:use",
        "OperationEntitlements": {
          "ai.chat.*": "ai:chat"
        },
        "NoLicenseOperations": [
          "auth.*",
          "billing.portal.*",
          "licensing.runtime.*"
        ]
      }
    }
  }
}
```

Then grant the expired or fallback plan only the reduced entitlement:

```json
{
  "Key": "hubbsly-expired-ai-chat",
  "DisplayName": "Expired Account AI Chat",
  "Entitlements": [ "ai:chat" ],
  "DefaultSeatLimit": 1
}
```

Service operations stay simple:

```csharp
[IBeamRequiresEntitlement("app:use")]
public sealed class PatientService
{
    private readonly IServiceOperationExecutor _operations;

    [IBeamOperation("patients.create")]
    public Task CreateAsync(CancellationToken ct)
        => _operations.ExecuteAsync(this, CreateCoreAsync, ct: ct);

    private Task CreateCoreAsync(CancellationToken ct)
    {
        // Create the patient.
        return Task.CompletedTask;
    }
}

public sealed class AiChatService
{
    private readonly IServiceOperationExecutor _operations;

    [IBeamOperation("ai.chat.messages.create")]
    [IBeamRequiresEntitlement("ai:chat")]
    public Task CreateMessageAsync(CancellationToken ct)
        => _operations.ExecuteAsync(this, CreateMessageCoreAsync, ct: ct);
}
```

The least-configuration rule is: set the default entitlement once, override the exceptional operation family, and make renewal/auth/billing routes no-license routes.

### Credit Packs

Credit packs are grants to a tenant credit account. They may come from a plan, a checkout add-on, manual support, or a provider event.

```csharp
await creditUsageRecorder.RecordUsageAsync(
    tenantId,
    new RecordCreditUsageRequest
    {
        CreditAccountId = accountId,
        BucketKey = "ai-chat",
        Amount = 12,
        OperationName = "ai.chat.complete",
        IdempotencyKey = requestId
    },
    ct);
```

Use credits for consumption. Use licensing to decide whether the customer may access the product or operation at all.

### Dynamic AI Usage

Some AI operations do not know final cost until the model, speech, document, or tool call returns. Use the policy mode that matches the customer's commercial contract.

| Policy | Best Fit | Runtime Behavior |
|---|---|---|
| `strict-prepaid` | Prepaid consumer or SMB plans | Reserve the max first. Deny before work if credits are insufficient. |
| `soft-overage` | Customers allowed to run past prepaid balance | Reserve an estimate, settle actual, then invoice or carry a negative balance outside the request path. |
| `fail-open-metering` | Trusted enterprise contracts | Run first, then record actual usage. |
| `cap-by-request` | Caller-controlled spend caps | Require `MaxCredits` and deny when projected usage exceeds it. |
| `streaming` | Long-running streaming work | Settle chunks or maintain a reservation as the stream progresses. |

The combined gate handles license checks, credit reservation, execution, settlement, and release-on-error:

```csharp
var result = await licenseCreditGate.ExecuteAsync(
    new LicenseCreditGateRequest
    {
        TenantId = tenantId,
        Subject = new LicenseSubject(LicenseSubjectTypes.User, userId.ToString("D")),
        Entitlement = "ai:chat",
        OperationName = "ai.chat.complete",
        CreditAccountId = accountId,
        CreditBucketKey = "ai-chat",
        EstimatedCredits = 10,
        MaxCredits = 50,
        CreditPolicyMode = CreditPolicyModes.StrictPrepaid
    },
    async ct =>
    {
        var response = await chat.CompleteAsync(prompt, ct);
        return new CreditMeasuredOperationResult<ChatResponse>(
            response,
            response.CreditsUsed);
    },
    ct);

if (!result.Allowed)
{
    // Route by result.Gate.DenialScope and result.Gate.DenialCode.
}
```

For streaming, call `ICreditPolicyService.RecordStreamingChunkAsync(...)` or equivalent host orchestration after each measured chunk. If the customer's max is reached, stop the stream server-side and return a normal spend-cap response.

## Client-Side Storage

Store only short-lived runtime guidance on the client:

- current tenant id and selected tenant display
- runtime license status
- entitlements for navigation and button states
- seat state
- credit summaries and last refresh time
- renewal/contact-admin hints

Do not store:

- authoritative license grants
- provider subscription state
- payment method details
- credit ledger entries as authority
- secrets, provider ids, or raw API keys

Recommended frontend behavior:

- Refresh bootstrap data after login, tenant switch, checkout return, seat changes, and visible denial responses.
- Refresh periodically for long sessions.
- Optimistically hide disabled features, but expect the server to return denials.
- Treat expiration dates as display hints. The server decides based on its own clock.
- Do not allow local state edits to bypass server checks.

## Server-Side Enforcement

Every protected write or expensive operation should enforce at least one of these:

- `ILicenseAuthorizer.RequireEntitlementAsync(...)`
- `ILicenseGate.CheckAsync(...)`
- `[IBeamRequiresEntitlement(...)]` with `AddIBeamLicensedServiceOperations()`
- `ILicenseCreditGate.ExecuteAsync(...)`
- `ICreditPolicyService` / `ICreditReservationService`

Example CRUD guard:

```csharp
public sealed class NotesService
{
    private readonly ILicenseAuthorizer _licenses;

    public async Task SaveAsync(Guid tenantId, Guid userId, Note note, CancellationToken ct)
    {
        await _licenses.RequireEntitlementAsync(
            tenantId,
            new LicenseSubject(LicenseSubjectTypes.User, userId.ToString("D")),
            "notes:write",
            ct);

        // Save the note.
    }
}
```

Example IBeam service-base guard:

```csharp
[IBeamRequiresEntitlement("notes:use")]
public sealed class NotesService : BaseServiceAsync<NoteEntity, NoteModel>
{
    private readonly IServiceOperationExecutor _operations;

    [IBeamOperation("notes.save")]
    [IBeamRequiresEntitlement("notes:write")]
    public Task<NoteModel> SaveNoteAsync(
        NoteModel note,
        Guid tenantId,
        CancellationToken ct)
        => _operations.ExecuteAsync(
            this,
            _ => SaveCoreAsync(note, ct),
            new ServiceOperationExecutionOptions
            {
                TenantId = tenantId,
                OperationName = "notes.save"
            },
            ct);
}
```

This keeps enforcement near the business operation instead of relying on controllers or frontend route guards.

## Migration From Existing Licensing Calls

Existing apps that already use `IBeam.Licensing` can migrate incrementally:

1. Keep current `ILicenseAuthorizer.RequireEntitlementAsync(...)` calls.
2. Add `ILicenseGate` when the UI/API needs structured denial reasons.
3. Add `LicenseRuntimeController` or the credit `bootstrap` endpoint for post-auth frontend guidance.
4. Move local license persistence behind `ILicensingStore`.
5. Add `IBeam.Commerce.Repositories.AzureTable` or a host provider for durable storage.
6. Add `IBeam.Billing` only when commercial records or provider webhooks need a neutral home.
7. Add `IBeam.Billing.Licensing` when subscriptions should automatically create, renew, suspend, or revoke licenses.
8. Add `IBeam.Credits` and `IBeam.Licensing.Credits` when operations need metered consumption.

The migration rule is simple: do not replace a working entitlement check just because billing or credits exists. Add the new layer at the point where it answers a different question.

## Operational Rules

- Keep provider secrets in secret stores, not plan or billing records.
- Use idempotency keys for billing provider events, credit reservations, and credit settlements.
- Prefer append-only credit ledger entries.
- Re-run runtime bootstrap after checkout, tenant switch, denial, or admin seat changes.
- Protect admin endpoints with tenant-admin policies or API credential scopes.
- Keep billing webhooks narrow: verify provider signature, record event, reconcile, return.
- Use audit logging on license, seat, billing, and credit mutations.
- Treat Azure Table commerce storage as the durable default provider, not as the only possible provider.
