using Microsoft.Extensions.Options;

namespace IBeam.Billing.Services;

public sealed class ConfigurationBillingOfferCatalogProvider : IBillingOfferCatalogProvider
{
    private readonly IReadOnlyList<BillingOfferInfo> _offers;

    public ConfigurationBillingOfferCatalogProvider(IOptions<BillingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _offers = BuildOffers(options.Value.Offers);
    }

    public Task<IReadOnlyList<BillingOfferInfo>> ListOffersAsync(CancellationToken ct = default)
        => Task.FromResult(_offers);

    public Task<BillingOfferInfo?> GetOfferAsync(string offerKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(offerKey))
            return Task.FromResult<BillingOfferInfo?>(null);

        var normalizedKey = offerKey.Trim();
        return Task.FromResult(
            _offers.FirstOrDefault(x => string.Equals(x.Key, normalizedKey, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<BillingOfferInfo> BuildOffers(IReadOnlyList<BillingOfferOptions> options)
    {
        var duplicate = options
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicate is not null)
            throw new InvalidOperationException($"Billing offer '{duplicate.Key}' is configured more than once.");

        return options
            .Select(x => BillingOfferInfo.Create(
                x.Key,
                x.ProductKey,
                x.PlanKey,
                x.DisplayName,
                x.Description,
                x.BillingPeriod,
                x.Currency,
                BillingOfferSeatPolicyInfo.Create(
                    x.SeatPolicy.DefaultTotalSeats,
                    x.SeatPolicy.MinimumTotalSeats,
                    x.SeatPolicy.MaximumTotalSeats,
                    x.SeatPolicy.SeatIncrement),
                BillingOfferPricingInfo.Create(
                    x.Pricing.BaseAmount,
                    x.Pricing.PerSeatAmount,
                    x.Pricing.Tiers
                        .Select(y => new BillingOfferPriceTierInfo(y.UpToTotalSeats, y.PerSeatAmount))
                        .ToList()),
                x.ProviderPrices
                    .Select(y => BillingPriceReferenceInfo.Create(
                        y.ProviderName,
                        y.PriceId,
                        x.ProductKey,
                        x.PlanKey,
                        x.Currency,
                        billingPeriod: x.BillingPeriod,
                        billingMode: y.BillingMode,
                        metadata: y.Metadata))
                    .ToList(),
                x.Metadata))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
