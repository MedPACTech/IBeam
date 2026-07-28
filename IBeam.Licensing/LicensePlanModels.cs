namespace IBeam.Licensing;

public sealed record LicensePlanInfo(
    string Key,
    string DisplayName,
    string? Description,
    IReadOnlyList<string> Entitlements,
    IReadOnlyDictionary<string, int> Limits,
    IReadOnlyDictionary<string, string> Metadata,
    bool IsConfigured = true)
{
    public string? ProductKey { get; init; }
    public string Classification { get; init; } = LicensePlanClassifications.Tenant;
    public int? Level { get; init; }
    public int? DefaultSeatLimit { get; init; }
    public IReadOnlyList<LicenseCreditGrantInfo> DefaultCreditGrants { get; init; } = [];
    public IReadOnlyList<LicenseProviderPriceInfo> ProviderPrices { get; init; } = [];
}

public sealed record LicenseProductInfo(
    string Key,
    string DisplayName,
    string? Description,
    IReadOnlyDictionary<string, string> Metadata,
    bool IsConfigured = true);

public sealed record LicenseCreditGrantInfo(
    string BucketKey,
    int Amount,
    string? DisplayName,
    string? Period,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record LicenseProviderPriceInfo(
    string ProviderName,
    string PriceId,
    string? Currency,
    decimal? UnitAmount,
    string? BillingPeriod,
    IReadOnlyDictionary<string, string> Metadata);

public static class LicensePlanClassifications
{
    public const string SingleUser = "single-user";
    public const string Tenant = "tenant";
    public const string Enterprise = "enterprise";
    public const string AddOn = "add-on";
}
