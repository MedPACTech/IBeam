# IBeam.Licensing.Credits

Optional integration package that combines `ILicenseGate` with `ICreditPolicyService`.

Use this package when a protected operation must require both a license entitlement and credits. Licensing and Credits remain standalone packages; this package only coordinates them.

```csharp
var result = await licenseCreditGate.ExecuteAsync(
    new LicenseCreditGateRequest
    {
        TenantId = tenantId,
        Subject = new LicenseSubject(LicenseSubjectTypes.User, userId),
        Entitlement = "ai:chat",
        OperationName = "ai.chat.complete",
        CreditAccountId = creditAccountId,
        CreditBucketKey = "ai-chat",
        EstimatedCredits = 10,
        MaxCredits = 50,
        CreditPolicyMode = CreditPolicyModes.StrictPrepaid
    },
    async ct =>
    {
        var response = await chat.CompleteAsync(prompt, ct);
        return new CreditMeasuredOperationResult<ChatResponse>(response, response.CreditsUsed);
    },
    ct);
```

If the entitlement or seat check fails, the operation is not executed. If the credit check fails, the operation is not executed. If the operation throws after a reservation was created, the reservation is released before the original exception is rethrown.
