namespace IBeam.Billing;

public sealed record BillingCheckoutAttemptInfo(
    Guid CheckoutAttemptId,
    Guid PurchaseId,
    string ProviderName,
    string ProviderCheckoutSessionId,
    string Status,
    DateTimeOffset CreatedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingCheckoutAttemptInfo Create(
        Guid purchaseId,
        string providerName,
        string providerCheckoutSessionId,
        string status,
        DateTimeOffset createdUtc,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (purchaseId == Guid.Empty)
            throw new ArgumentException("Purchase id is required.", nameof(purchaseId));
        var normalizedProvider = BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName)).ToLowerInvariant();
        var normalizedSession = BillingPriceReferenceInfo.NormalizeRequired(providerCheckoutSessionId, nameof(providerCheckoutSessionId));
        var idBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{purchaseId:N}:{normalizedProvider}:{normalizedSession}"));
        return new BillingCheckoutAttemptInfo(
            new Guid(idBytes.AsSpan(0, 16)),
            purchaseId,
            normalizedProvider,
            normalizedSession,
            BillingCheckoutSessionStatuses.Normalize(status),
            createdUtc,
            BillingPriceReferenceInfo.NormalizeMetadata(metadata));
    }
}
