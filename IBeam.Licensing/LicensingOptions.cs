namespace IBeam.Licensing;

public sealed class LicensingOptions
{
    public const string SectionName = "IBeam:Licensing";

    public List<LicenseProductOptions> Products { get; set; } = [];
    public List<LicensePlanOptions> Plans { get; set; } = [];
    public LicensedServiceOperationOptions ServiceOperations { get; set; } = new();

    public void Validate()
    {
        ServiceOperations.Normalize();

        Products = Products
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x =>
            {
                x.Normalize();
                return x;
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        Plans = Plans
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x =>
            {
                x.Normalize();
                return x;
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }
}

public sealed class LicensedServiceOperationOptions
{
    public string? DefaultEntitlement { get; set; }
    public Dictionary<string, string> OperationEntitlements { get; set; } = [];
    public List<string> NoLicenseOperations { get; set; } = [];

    internal void Normalize()
    {
        DefaultEntitlement = string.IsNullOrWhiteSpace(DefaultEntitlement) ? null : DefaultEntitlement.Trim();
        OperationEntitlements = OperationEntitlements
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        NoLicenseOperations = NoLicenseOperations
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class LicenseProductOptions
{
    public string Key { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];

    internal void Normalize()
    {
        Key = Key.Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName.Trim();
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        Metadata = NormalizeMetadata(Metadata);
    }

    private static Dictionary<string, string> NormalizeMetadata(Dictionary<string, string> values)
        => values
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}

public sealed class LicensePlanOptions
{
    public string Key { get; set; } = string.Empty;
    public string? ProductKey { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Classification { get; set; }
    public int? Level { get; set; }
    public List<string> Entitlements { get; set; } = [];
    public Dictionary<string, int> Limits { get; set; } = [];
    public int? DefaultSeatLimit { get; set; }
    public List<LicenseCreditGrantOptions> DefaultCreditGrants { get; set; } = [];
    public List<LicenseProviderPriceOptions> ProviderPrices { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];

    internal void Normalize()
    {
        Key = Key.Trim();
        ProductKey = string.IsNullOrWhiteSpace(ProductKey) ? null : ProductKey.Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName.Trim();
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        Classification = NormalizeClassification(Classification);
        if (Level <= 0)
            Level = null;
        if (DefaultSeatLimit <= 0)
            DefaultSeatLimit = null;
        Entitlements = Entitlements
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Limits = NormalizeLimits(Limits);
        DefaultCreditGrants = DefaultCreditGrants
            .Where(x => !string.IsNullOrWhiteSpace(x.BucketKey) && x.Amount > 0)
            .Select(x =>
            {
                x.Normalize();
                return x;
            })
            .ToList();
        ProviderPrices = ProviderPrices
            .Where(x => !string.IsNullOrWhiteSpace(x.ProviderName) && !string.IsNullOrWhiteSpace(x.PriceId))
            .Select(x =>
            {
                x.Normalize();
                return x;
            })
            .ToList();
        Metadata = NormalizeMetadata(Metadata);
    }

    private static string NormalizeClassification(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? LicensePlanClassifications.Tenant
            : value.Trim().ToLowerInvariant();

    private static Dictionary<string, int> NormalizeLimits(Dictionary<string, int> values)
        => values
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> NormalizeMetadata(Dictionary<string, string> values)
        => values
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}

public sealed class LicenseCreditGrantOptions
{
    public string BucketKey { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string? DisplayName { get; set; }
    public string? Period { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];

    internal void Normalize()
    {
        BucketKey = BucketKey.Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim();
        Period = string.IsNullOrWhiteSpace(Period) ? null : Period.Trim();
        Metadata = NormalizeMetadata(Metadata);
    }

    private static Dictionary<string, string> NormalizeMetadata(Dictionary<string, string> values)
        => values
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}

public sealed class LicenseProviderPriceOptions
{
    public string ProviderName { get; set; } = string.Empty;
    public string PriceId { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public decimal? UnitAmount { get; set; }
    public string? BillingPeriod { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];

    internal void Normalize()
    {
        ProviderName = ProviderName.Trim();
        PriceId = PriceId.Trim();
        Currency = string.IsNullOrWhiteSpace(Currency) ? null : Currency.Trim().ToUpperInvariant();
        BillingPeriod = string.IsNullOrWhiteSpace(BillingPeriod) ? null : BillingPeriod.Trim();
        Metadata = NormalizeMetadata(Metadata);
    }

    private static Dictionary<string, string> NormalizeMetadata(Dictionary<string, string> values)
        => values
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
