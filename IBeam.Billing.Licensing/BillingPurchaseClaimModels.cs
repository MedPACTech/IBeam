using IBeam.Licensing;

namespace IBeam.Billing.Licensing;

public interface IBillingPurchaseClaimService
{
    Task<IssuedBillingPurchaseClaimInfo> IssueAsync(Guid purchaseId, CancellationToken ct = default);
    Task<ClaimedBillingPurchaseLicenseInfo> ClaimAsync(ClaimBillingPurchaseLicenseRequest request, CancellationToken ct = default);
}

public interface IBillingPurchaseClaimStore
{
    Task<BillingPurchaseClaimRecord?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<BillingPurchaseClaimRecord?> GetByPurchaseAsync(Guid purchaseId, CancellationToken ct = default);
    Task<BillingPurchaseClaimRecord> SaveIssuedAsync(BillingPurchaseClaimRecord record, CancellationToken ct = default);
    Task<BillingPurchaseClaimRecord?> TryClaimAsync(
        string tokenHash,
        Guid tenantId,
        Guid userId,
        DateTimeOffset claimedUtc,
        CancellationToken ct = default);
}

public sealed record BillingPurchaseClaimRecord(
    Guid ClaimId,
    Guid PurchaseId,
    Guid LicenseKey,
    string TokenHash,
    string BuyerEmailHash,
    DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc,
    Guid? ClaimedTenantId,
    Guid? ClaimedUserId,
    DateTimeOffset? ClaimedUtc);

public sealed record IssuedBillingPurchaseClaimInfo(
    Guid ClaimId,
    Guid PurchaseId,
    string ClaimToken,
    DateTimeOffset ExpiresUtc);

public sealed class ClaimBillingPurchaseLicenseRequest
{
    public string ClaimToken { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string VerifiedEmail { get; set; } = string.Empty;
}

public sealed record ClaimedBillingPurchaseLicenseInfo(
    Guid ClaimId,
    Guid PurchaseId,
    Guid TenantId,
    Guid UserId,
    TenantLicenseInfo License,
    IReadOnlyList<LicenseSeatAssignmentInfo> Assignments,
    DateTimeOffset ClaimedUtc,
    bool IsRetry);

public sealed class BillingPurchaseClaimOptions
{
    public int TokenLifetimeMinutes { get; set; } = 30;

    public void Validate()
    {
        if (TokenLifetimeMinutes is < 5 or > 1440)
            throw new InvalidOperationException("Purchase claim token lifetime must be between 5 minutes and 24 hours.");
    }
}
