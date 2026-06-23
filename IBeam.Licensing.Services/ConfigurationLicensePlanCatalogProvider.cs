using Microsoft.Extensions.Options;

namespace IBeam.Licensing.Services;

public sealed class ConfigurationLicensePlanCatalogProvider : ILicensePlanCatalogProvider
{
    private readonly LicensingOptions _options;

    public ConfigurationLicensePlanCatalogProvider(IOptions<LicensingOptions> options)
    {
        _options = options.Value;
        _options.Validate();
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
                    x.Metadata))
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
