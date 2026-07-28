namespace IBeam.Credits;

public static class CreditAccountStatuses
{
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Closed = "closed";
    public const string Unknown = "unknown";

    public static string Normalize(string? status)
        => CreditNormalization.NormalizeKnown(status, Active);
}

public static class CreditGrantTypes
{
    public const string OneTime = "one-time";
    public const string Monthly = "monthly";
    public const string Expiring = "expiring";
    public const string Adjustment = "adjustment";

    public static string Normalize(string? type)
        => CreditNormalization.NormalizeKnown(type, OneTime);
}

public static class CreditRolloverPolicies
{
    public const string None = "none";
    public const string Rollover = "rollover";
    public const string CappedRollover = "capped-rollover";

    public static string Normalize(string? policy)
        => CreditNormalization.NormalizeKnown(policy, None);
}

public static class CreditLedgerEntryTypes
{
    public const string Grant = "grant";
    public const string Debit = "debit";
    public const string Expiration = "expiration";
    public const string Adjustment = "adjustment";

    public static string Normalize(string? type)
        => CreditNormalization.NormalizeKnown(type, Adjustment);
}

public static class CreditNormalization
{
    public static string NormalizeRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", name);

        return value.Trim();
    }

    public static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string NormalizeKey(string? value, string name)
        => NormalizeRequired(value, name).ToLowerInvariant().Replace("_", "-");

    public static string NormalizeKnown(string? value, string defaultValue)
        => string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim().ToLowerInvariant().Replace("_", "-");

    public static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
        => metadata?
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
