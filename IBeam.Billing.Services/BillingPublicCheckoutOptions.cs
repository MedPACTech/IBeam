namespace IBeam.Billing.Services;

public sealed class BillingPublicCheckoutOptions
{
    public string DefaultProviderName { get; set; } = string.Empty;
    public List<string> AllowedReturnOrigins { get; set; } = [];
    public string StatusTokenSigningKey { get; set; } = string.Empty;
    public int StatusTokenLifetimeMinutes { get; set; } = 30;
    public int PurchaseLifetimeMinutes { get; set; } = 60;

    internal void Validate()
    {
        DefaultProviderName = BillingPriceReferenceInfo.NormalizeRequired(DefaultProviderName, nameof(DefaultProviderName)).ToLowerInvariant();
        if (StatusTokenSigningKey.Length < 32)
            throw new InvalidOperationException("IBeam:Billing:PublicCheckout:StatusTokenSigningKey must contain at least 32 characters.");
        if (StatusTokenLifetimeMinutes is < 1 or > 1440)
            throw new InvalidOperationException("StatusTokenLifetimeMinutes must be between 1 and 1440.");
        if (PurchaseLifetimeMinutes is < 5 or > 10080)
            throw new InvalidOperationException("PurchaseLifetimeMinutes must be between 5 and 10080.");

        AllowedReturnOrigins = AllowedReturnOrigins
            .Select(x => NormalizeOrigin(new Uri(x, UriKind.Absolute)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (AllowedReturnOrigins.Count == 0)
            throw new InvalidOperationException("At least one public checkout return origin is required.");
    }

    internal static string NormalizeOrigin(Uri uri)
    {
        CreateBillingCheckoutSessionRequest.NormalizeAbsoluteHttpUri(uri, nameof(uri));
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
