namespace IBeam.Credits;

public sealed record CreditBucketInfo(
    string Key,
    string DisplayName,
    string? Description,
    string? UnitName,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static CreditBucketInfo Create(
        string key,
        string? displayName = null,
        string? description = null,
        string? unitName = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var normalizedKey = CreditNormalization.NormalizeKey(key, nameof(key));
        return new(
            normalizedKey,
            CreditNormalization.NormalizeOptional(displayName) ?? normalizedKey,
            CreditNormalization.NormalizeOptional(description),
            CreditNormalization.NormalizeOptional(unitName),
            CreditNormalization.NormalizeMetadata(metadata));
    }
}
