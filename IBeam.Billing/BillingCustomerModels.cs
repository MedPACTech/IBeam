namespace IBeam.Billing;

public sealed record BillingCustomerRecord(
    Guid BillingCustomerId,
    Guid TenantId,
    Guid? UserId,
    string DisplayName,
    string? Email,
    string BillingMode,
    string Status,
    string? ProviderName,
    string? ProviderCustomerId,
    BillingPaymentMethodReferenceInfo? DefaultPaymentMethod,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public BillingCustomerInfo ToInfo()
        => BillingCustomerInfo.FromRecord(this);
}

public sealed record BillingCustomerInfo(
    Guid BillingCustomerId,
    Guid TenantId,
    Guid? UserId,
    string DisplayName,
    string? Email,
    string BillingMode,
    string Status,
    string? ProviderName,
    string? ProviderCustomerId,
    BillingPaymentMethodReferenceInfo? DefaultPaymentMethod,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingCustomerInfo FromRecord(BillingCustomerRecord record)
        => new(
            record.BillingCustomerId,
            record.TenantId,
            record.UserId,
            record.DisplayName,
            record.Email,
            BillingModes.Normalize(record.BillingMode),
            BillingCustomerStatuses.Normalize(record.Status),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderName),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderCustomerId),
            record.DefaultPaymentMethod,
            record.CreatedUtc,
            record.UpdatedUtc,
            BillingPriceReferenceInfo.NormalizeMetadata(record.Metadata));
}

public sealed class CreateBillingCustomerRequest
{
    public Guid? UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? BillingMode { get; set; }
    public string? Status { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderCustomerId { get; set; }
    public BillingPaymentMethodReferenceInfo? DefaultPaymentMethod { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class UpdateBillingCustomerRequest
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? BillingMode { get; set; }
    public string? Status { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderCustomerId { get; set; }
    public BillingPaymentMethodReferenceInfo? DefaultPaymentMethod { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
