using IBeam.Licensing;

namespace IBeam.Billing.Licensing;

public interface ICommerceAdministrationService
{
    Task<CommerceAdministrationSnapshot> InspectAsync(Guid tenantId, Guid purchaseId, CancellationToken ct = default);
    Task<BillingWebhookProcessingInfo> RetryVerifiedEventAsync(Guid tenantId, string providerName, string providerEventId, CommerceRecoveryRequest request, CancellationToken ct = default);
    Task<BillingPurchaseInfo> RetryFulfillmentAsync(Guid tenantId, Guid purchaseId, CommerceRecoveryRequest request, CancellationToken ct = default);
    Task<IssuedBillingPurchaseClaimInfo> ResendClaimAsync(Guid tenantId, Guid purchaseId, CommerceRecoveryRequest request, CancellationToken ct = default);
    Task<BillingPurchaseInfo> ApplyManualCorrectionAsync(Guid tenantId, Guid purchaseId, ManualCommerceCorrectionRequest request, CancellationToken ct = default);
}

public sealed record CommerceAdministrationSnapshot(
    CommercePurchaseSummary Purchase,
    BillingSubscriptionInfo? Subscription,
    TenantLicenseInfo? License,
    IReadOnlyList<LicenseSeatAssignmentInfo> SeatAssignments,
    CommerceClaimSummary? Claim,
    IReadOnlyList<BillingSubscriptionProviderBindingInfo> ProviderBindings);

public sealed record CommercePurchaseSummary(
    Guid PurchaseId,
    Guid CorrelationId,
    Guid? TenantId,
    Guid? UserId,
    Guid? LicenseKey,
    string? BuyerEmail,
    string OfferKey,
    string PlanKey,
    int TotalSeats,
    string Currency,
    decimal AmountTotal,
    string Status,
    string? ProviderName,
    string? ProviderSubscriptionId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? ExpiresUtc);

public sealed record CommerceClaimSummary(
    Guid ClaimId,
    Guid PurchaseId,
    Guid LicenseKey,
    DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc,
    Guid? ClaimedTenantId,
    Guid? ClaimedUserId,
    DateTimeOffset? ClaimedUtc);

public class CommerceRecoveryRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class ManualCommerceCorrectionRequest : CommerceRecoveryRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
