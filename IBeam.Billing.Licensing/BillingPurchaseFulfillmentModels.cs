namespace IBeam.Billing.Licensing;

public interface IBillingPurchaseLicenseFulfillmentService
{
    Task<BillingPurchaseLicenseInfo> FulfillAsync(
        FulfillBillingPurchaseLicenseRequest request,
        CancellationToken ct = default);
}

public sealed class FulfillBillingPurchaseLicenseRequest
{
    public Guid PurchaseId { get; set; }
    public Guid? ExistingLicenseKey { get; set; }
}

public sealed record BillingPurchaseLicenseInfo(
    Guid LicenseKey,
    Guid PurchaseId,
    string ProductKey,
    string PlanKey,
    int TotalSeats,
    string Status,
    string? ProviderName,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    DateTimeOffset FulfilledUtc);
