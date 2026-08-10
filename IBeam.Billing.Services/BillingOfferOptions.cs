namespace IBeam.Billing.Services;

public sealed class BillingOptions
{
    public const string SectionName = "IBeam:Billing";

    public List<BillingOfferOptions> Offers { get; set; } = [];
}

public sealed class BillingOfferOptions
{
    public string Key { get; set; } = string.Empty;
    public string ProductKey { get; set; } = string.Empty;
    public string PlanKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string BillingPeriod { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public BillingOfferSeatPolicyOptions SeatPolicy { get; set; } = new();
    public BillingOfferPricingOptions Pricing { get; set; } = new();
    public List<BillingOfferProviderPriceOptions> ProviderPrices { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class BillingOfferSeatPolicyOptions
{
    public int DefaultTotalSeats { get; set; }
    public int MinimumTotalSeats { get; set; }
    public int? MaximumTotalSeats { get; set; }
    public int SeatIncrement { get; set; } = 1;
}

public sealed class BillingOfferPricingOptions
{
    public decimal BaseAmount { get; set; }
    public decimal PerSeatAmount { get; set; }
    public List<BillingOfferPriceTierOptions> Tiers { get; set; } = [];
}

public sealed class BillingOfferPriceTierOptions
{
    public int? UpToTotalSeats { get; set; }
    public decimal PerSeatAmount { get; set; }
}

public sealed class BillingOfferProviderPriceOptions
{
    public string ProviderName { get; set; } = string.Empty;
    public string PriceId { get; set; } = string.Empty;
    public string? BillingMode { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}
