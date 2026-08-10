namespace IBeam.Billing;

public sealed record BillingOfferInfo(
    string Key,
    string ProductKey,
    string PlanKey,
    string DisplayName,
    string? Description,
    string BillingPeriod,
    string Currency,
    BillingOfferSeatPolicyInfo SeatPolicy,
    BillingOfferPricingInfo Pricing,
    IReadOnlyList<BillingPriceReferenceInfo> ProviderPrices,
    IReadOnlyDictionary<string, string> Metadata)
{
    public int LicenseQuantity => 1;

    public static BillingOfferInfo Create(
        string key,
        string productKey,
        string planKey,
        string displayName,
        string? description,
        string billingPeriod,
        string currency,
        BillingOfferSeatPolicyInfo seatPolicy,
        BillingOfferPricingInfo pricing,
        IReadOnlyList<BillingPriceReferenceInfo>? providerPrices = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(seatPolicy);
        ArgumentNullException.ThrowIfNull(pricing);

        var normalizedCurrency = BillingPriceReferenceInfo.NormalizeCurrency(currency)
            ?? throw new ArgumentException("Currency is required.", nameof(currency));

        return new BillingOfferInfo(
            BillingPriceReferenceInfo.NormalizeRequired(key, nameof(key)),
            BillingPriceReferenceInfo.NormalizeRequired(productKey, nameof(productKey)),
            BillingPriceReferenceInfo.NormalizeRequired(planKey, nameof(planKey)),
            BillingPriceReferenceInfo.NormalizeRequired(displayName, nameof(displayName)),
            BillingPriceReferenceInfo.NormalizeOptional(description),
            BillingPriceReferenceInfo.NormalizeRequired(billingPeriod, nameof(billingPeriod)).ToLowerInvariant(),
            normalizedCurrency,
            seatPolicy,
            pricing,
            NormalizeProviderPrices(providerPrices),
            BillingPriceReferenceInfo.NormalizeMetadata(metadata));
    }

    public BillingOfferQuoteInfo Quote(int? requestedTotalSeats = null)
    {
        var totalSeats = SeatPolicy.NormalizeTotalSeats(requestedTotalSeats);
        return new BillingOfferQuoteInfo(Key, LicenseQuantity, totalSeats, Currency, Pricing.CalculateTotal(totalSeats));
    }

    private static IReadOnlyList<BillingPriceReferenceInfo> NormalizeProviderPrices(
        IReadOnlyList<BillingPriceReferenceInfo>? providerPrices)
    {
        var prices = providerPrices ?? [];
        var duplicate = prices
            .GroupBy(x => $"{x.ProviderName.Trim()}:{x.PriceId.Trim()}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"Provider price '{duplicate.Key}' is configured more than once.", nameof(providerPrices));

        return prices
            .Select(x => BillingPriceReferenceInfo.Create(
                x.ProviderName,
                x.PriceId,
                x.ProductKey,
                x.PlanKey,
                x.Currency,
                x.UnitAmount,
                x.BillingPeriod,
                x.BillingMode,
                x.Metadata))
            .ToList();
    }
}

public sealed record BillingOfferSeatPolicyInfo(
    int DefaultTotalSeats,
    int MinimumTotalSeats,
    int? MaximumTotalSeats,
    int SeatIncrement)
{
    public static BillingOfferSeatPolicyInfo Create(
        int defaultTotalSeats,
        int minimumTotalSeats,
        int? maximumTotalSeats = null,
        int seatIncrement = 1)
    {
        if (minimumTotalSeats <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumTotalSeats), "Minimum total seats must be positive.");
        if (defaultTotalSeats < minimumTotalSeats)
            throw new ArgumentOutOfRangeException(nameof(defaultTotalSeats), "Default total seats cannot be below the minimum.");
        if (maximumTotalSeats is not null && maximumTotalSeats < defaultTotalSeats)
            throw new ArgumentOutOfRangeException(nameof(maximumTotalSeats), "Maximum total seats cannot be below the default.");
        if (seatIncrement <= 0)
            throw new ArgumentOutOfRangeException(nameof(seatIncrement), "Seat increment must be positive.");
        if ((defaultTotalSeats - minimumTotalSeats) % seatIncrement != 0)
            throw new ArgumentException("Default total seats must align with the configured seat increment.", nameof(defaultTotalSeats));

        return new BillingOfferSeatPolicyInfo(
            defaultTotalSeats,
            minimumTotalSeats,
            maximumTotalSeats,
            seatIncrement);
    }

    public int NormalizeTotalSeats(int? requestedTotalSeats)
    {
        var totalSeats = requestedTotalSeats ?? DefaultTotalSeats;
        if (totalSeats < MinimumTotalSeats)
            throw new BillingException($"Total seat quantity must be at least {MinimumTotalSeats}.");
        if (MaximumTotalSeats is not null && totalSeats > MaximumTotalSeats)
            throw new BillingException($"Total seat quantity cannot exceed {MaximumTotalSeats}.");
        if ((totalSeats - MinimumTotalSeats) % SeatIncrement != 0)
            throw new BillingException($"Total seat quantity must increase in increments of {SeatIncrement} from {MinimumTotalSeats}.");

        return totalSeats;
    }
}

public sealed record BillingOfferPricingInfo(
    decimal BaseAmount,
    decimal PerSeatAmount,
    IReadOnlyList<BillingOfferPriceTierInfo> Tiers)
{
    public static BillingOfferPricingInfo Create(
        decimal baseAmount = 0m,
        decimal perSeatAmount = 0m,
        IReadOnlyList<BillingOfferPriceTierInfo>? tiers = null)
    {
        if (baseAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(baseAmount), "Base amount cannot be negative.");
        if (perSeatAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(perSeatAmount), "Per-seat amount cannot be negative.");

        var normalizedTiers = NormalizeTiers(tiers);
        if (baseAmount == 0m && perSeatAmount == 0m && normalizedTiers.Count == 0)
            throw new ArgumentException("At least one price component is required.");

        return new BillingOfferPricingInfo(baseAmount, perSeatAmount, normalizedTiers);
    }

    public decimal CalculateTotal(int totalSeats)
    {
        if (totalSeats <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalSeats), "Total seats must be positive.");

        if (Tiers.Count == 0)
            return BaseAmount + (PerSeatAmount * totalSeats);

        var total = BaseAmount;
        var previousLimit = 0;
        var remainingSeats = totalSeats;

        foreach (var tier in Tiers)
        {
            var tierCapacity = tier.UpToTotalSeats is null
                ? remainingSeats
                : tier.UpToTotalSeats.Value - previousLimit;
            var seatsInTier = Math.Min(remainingSeats, tierCapacity);
            total += seatsInTier * tier.PerSeatAmount;
            remainingSeats -= seatsInTier;

            if (remainingSeats == 0)
                return total;

            previousLimit = tier.UpToTotalSeats!.Value;
        }

        if (PerSeatAmount > 0m)
            return total + (remainingSeats * PerSeatAmount);

        throw new BillingException($"No price tier covers a total seat quantity of {totalSeats}.");
    }

    private static IReadOnlyList<BillingOfferPriceTierInfo> NormalizeTiers(
        IReadOnlyList<BillingOfferPriceTierInfo>? tiers)
    {
        if (tiers is null || tiers.Count == 0)
            return [];

        var normalized = tiers.ToList();
        for (var index = 0; index < normalized.Count; index++)
        {
            var tier = normalized[index];
            if (tier.PerSeatAmount < 0m)
                throw new ArgumentOutOfRangeException(nameof(tiers), "Tier per-seat amount cannot be negative.");
            if (tier.UpToTotalSeats <= 0)
                throw new ArgumentOutOfRangeException(nameof(tiers), "Tier seat limits must be positive.");
            if (index > 0 && normalized[index - 1].UpToTotalSeats is null)
                throw new ArgumentException("An unlimited price tier must be last.", nameof(tiers));
            if (index > 0 && tier.UpToTotalSeats is not null &&
                tier.UpToTotalSeats <= normalized[index - 1].UpToTotalSeats)
                throw new ArgumentException("Price tier limits must be strictly increasing.", nameof(tiers));
        }

        return normalized;
    }
}

public sealed record BillingOfferPriceTierInfo(int? UpToTotalSeats, decimal PerSeatAmount);

public sealed record BillingOfferQuoteInfo(
    string OfferKey,
    int LicenseQuantity,
    int TotalSeats,
    string Currency,
    decimal TotalAmount);
