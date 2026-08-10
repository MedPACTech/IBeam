using System.Security.Cryptography;

namespace IBeam.Billing.Licensing;

public sealed class BillingPurchaseLicenseFulfillmentService :
    IBillingPurchaseLicenseFulfillmentService,
    IBillingPaidPurchaseHandler
{
    private static readonly Guid LicenseNamespace = Guid.Parse("eb4c1d38-0fc6-48ad-a0ba-999c2e255cc9");
    private readonly IBillingPurchaseService _purchases;

    public BillingPurchaseLicenseFulfillmentService(IBillingPurchaseService purchases)
    {
        _purchases = purchases;
    }

    public async Task<BillingPurchaseLicenseInfo> FulfillAsync(
        FulfillBillingPurchaseLicenseRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PurchaseId == Guid.Empty)
            throw new BillingException("purchaseId is required.");

        var existing = await _purchases.GetPurchaseAsync(request.PurchaseId, ct).ConfigureAwait(false)
                       ?? throw new BillingException("Purchase was not found.");
        var licenseKey = existing.LicenseKey
                         ?? NormalizeExistingKey(request.ExistingLicenseKey)
                         ?? CreateLicenseKey(existing.PurchaseId);
        var fulfilled = await _purchases.FulfillPaidPurchaseAsync(existing.PurchaseId, licenseKey, ct).ConfigureAwait(false);
        return new BillingPurchaseLicenseInfo(
            licenseKey,
            fulfilled.PurchaseId,
            fulfilled.ProductKey,
            fulfilled.PlanKey,
            fulfilled.TotalSeats,
            fulfilled.Status,
            fulfilled.ProviderName,
            fulfilled.ProviderCustomerId,
            fulfilled.ProviderSubscriptionId,
            fulfilled.FulfilledUtc!.Value);
    }

    public async Task HandlePaidPurchaseAsync(BillingPurchaseInfo purchase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        await FulfillAsync(new FulfillBillingPurchaseLicenseRequest { PurchaseId = purchase.PurchaseId }, ct)
            .ConfigureAwait(false);
    }

    private static Guid? NormalizeExistingKey(Guid? licenseKey)
        => licenseKey is null || licenseKey == Guid.Empty ? null : licenseKey;

    private static Guid CreateLicenseKey(Guid purchaseId)
    {
        Span<byte> input = stackalloc byte[32];
        LicenseNamespace.TryWriteBytes(input[..16]);
        purchaseId.TryWriteBytes(input[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash[..16]);
    }
}
