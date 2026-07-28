namespace IBeam.Billing.Services;

internal static class BillingServiceValidation
{
    public static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new BillingException("tenantId is required.");
    }

    public static void ValidateId(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new BillingException($"{name} is required.");
    }

    public static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BillingException($"{name} is required.");

        return value.Trim();
    }

    public static string RequiredOrExisting(string? value, string existing)
        => string.IsNullOrWhiteSpace(value) ? existing : value.Trim();

    public static string? OptionalOrExisting(string? value, string? existing)
        => string.IsNullOrWhiteSpace(value) ? existing : value.Trim();

    public static IReadOnlyDictionary<string, string> MetadataOrExisting(
        IReadOnlyDictionary<string, string>? value,
        IReadOnlyDictionary<string, string> existing)
        => value is null ? existing : BillingPriceReferenceInfo.NormalizeMetadata(value);
}
