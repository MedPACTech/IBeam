using Microsoft.Extensions.Options;

namespace IBeam.Licensing.Services;

public sealed class ConfigurationLicensePlanCatalogProvider :
    ILicensePlanCatalogProvider,
    ILicenseProductCatalogProvider
{
    private readonly LicensingOptions _options;

    public ConfigurationLicensePlanCatalogProvider(IOptions<LicensingOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public Task<IReadOnlyList<LicenseProductInfo>> ListProductsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LicenseProductInfo>>(
            _options.Products
                .Select(x => new LicenseProductInfo(
                    x.Key,
                    x.DisplayName ?? x.Key,
                    x.Description,
                    x.Metadata))
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());

    public async Task<LicenseProductInfo?> GetProductAsync(string productKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(productKey))
            return null;

        var products = await ListProductsAsync(ct).ConfigureAwait(false);
        return products.FirstOrDefault(x => string.Equals(x.Key, productKey.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public Task<IReadOnlyList<LicensePlanInfo>> ListPlansAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LicensePlanInfo>>(
            _options.Plans
                .Select(x => new LicensePlanInfo(
                    x.Key,
                    x.DisplayName ?? x.Key,
                    x.Description,
                    x.Entitlements,
                    x.Limits,
                    x.Metadata)
                {
                    ProductKey = x.ProductKey,
                    Classification = x.Classification ?? LicensePlanClassifications.Tenant,
                    Level = x.Level,
                    DefaultSeatLimit = x.DefaultSeatLimit,
                    DefaultCreditGrants = x.DefaultCreditGrants
                        .Select(y => new LicenseCreditGrantInfo(
                            y.BucketKey,
                            y.Amount,
                            y.DisplayName,
                            y.Period,
                            y.Metadata))
                        .ToList(),
                    ProviderPrices = x.ProviderPrices
                        .Select(y => new LicenseProviderPriceInfo(
                            y.ProviderName,
                            y.PriceId,
                            y.Currency,
                            y.UnitAmount,
                            y.BillingPeriod,
                            y.Metadata))
                        .ToList()
                })
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());

    public async Task<LicensePlanInfo?> GetPlanAsync(string planKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(planKey))
            return null;

        var plans = await ListPlansAsync(ct).ConfigureAwait(false);
        return plans.FirstOrDefault(x => string.Equals(x.Key, planKey.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
