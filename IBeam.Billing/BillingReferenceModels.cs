namespace IBeam.Billing;

public sealed record BillingPriceReferenceInfo(
    string ProviderName,
    string PriceId,
    string? ProductKey,
    string? PlanKey,
    string? Currency,
    decimal? UnitAmount,
    string? BillingPeriod,
    string? BillingMode,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingPriceReferenceInfo Create(
        string providerName,
        string priceId,
        string? productKey = null,
        string? planKey = null,
        string? currency = null,
        decimal? unitAmount = null,
        string? billingPeriod = null,
        string? billingMode = null,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new(
            NormalizeRequired(providerName, nameof(providerName)),
            NormalizeRequired(priceId, nameof(priceId)),
            NormalizeOptional(productKey),
            NormalizeOptional(planKey),
            NormalizeCurrency(currency),
            unitAmount,
            NormalizeOptional(billingPeriod),
            string.IsNullOrWhiteSpace(billingMode) ? null : BillingModes.Normalize(billingMode),
            NormalizeMetadata(metadata));

    public static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);

        return value.Trim();
    }

    public static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();

    public static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
        => metadata?
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record BillingPaymentMethodReferenceInfo(
    string ProviderName,
    string PaymentMethodId,
    string? Type,
    string? DisplayName,
    string? Brand,
    string? LastFour,
    int? ExpiresMonth,
    int? ExpiresYear,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingPaymentMethodReferenceInfo Create(
        string providerName,
        string paymentMethodId,
        string? type = null,
        string? displayName = null,
        string? brand = null,
        string? lastFour = null,
        int? expiresMonth = null,
        int? expiresYear = null,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new(
            BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName)),
            BillingPriceReferenceInfo.NormalizeRequired(paymentMethodId, nameof(paymentMethodId)),
            BillingPriceReferenceInfo.NormalizeOptional(type),
            BillingPriceReferenceInfo.NormalizeOptional(displayName),
            BillingPriceReferenceInfo.NormalizeOptional(brand),
            BillingPriceReferenceInfo.NormalizeOptional(lastFour),
            expiresMonth,
            expiresYear,
            BillingPriceReferenceInfo.NormalizeMetadata(metadata));
}
