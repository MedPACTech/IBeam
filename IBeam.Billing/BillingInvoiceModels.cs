namespace IBeam.Billing;

public sealed record BillingInvoiceRecord(
    Guid BillingInvoiceId,
    Guid TenantId,
    Guid? UserId,
    Guid BillingCustomerId,
    Guid? BillingSubscriptionId,
    string BillingMode,
    string Status,
    string? ProviderName,
    string? ProviderInvoiceId,
    string? InvoiceNumber,
    string Currency,
    decimal AmountDue,
    decimal AmountPaid,
    DateTimeOffset? DueUtc,
    DateTimeOffset? PaidUtc,
    string? HostedInvoiceUrl,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public BillingInvoiceInfo ToInfo()
        => BillingInvoiceInfo.FromRecord(this);
}

public sealed record BillingInvoiceInfo(
    Guid BillingInvoiceId,
    Guid TenantId,
    Guid? UserId,
    Guid BillingCustomerId,
    Guid? BillingSubscriptionId,
    string BillingMode,
    string Status,
    string? ProviderName,
    string? ProviderInvoiceId,
    string? InvoiceNumber,
    string Currency,
    decimal AmountDue,
    decimal AmountPaid,
    DateTimeOffset? DueUtc,
    DateTimeOffset? PaidUtc,
    string? HostedInvoiceUrl,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingInvoiceInfo FromRecord(BillingInvoiceRecord record)
        => new(
            record.BillingInvoiceId,
            record.TenantId,
            record.UserId,
            record.BillingCustomerId,
            record.BillingSubscriptionId,
            BillingModes.Normalize(record.BillingMode),
            BillingInvoiceStatuses.Normalize(record.Status),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderName),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderInvoiceId),
            BillingPriceReferenceInfo.NormalizeOptional(record.InvoiceNumber),
            BillingPriceReferenceInfo.NormalizeCurrency(record.Currency) ?? "USD",
            record.AmountDue,
            record.AmountPaid,
            record.DueUtc,
            record.PaidUtc,
            BillingPriceReferenceInfo.NormalizeOptional(record.HostedInvoiceUrl),
            record.CreatedUtc,
            record.UpdatedUtc,
            BillingPriceReferenceInfo.NormalizeMetadata(record.Metadata));
}

public sealed class CreateBillingInvoiceRequest
{
    public Guid BillingCustomerId { get; set; }
    public Guid? BillingSubscriptionId { get; set; }
    public Guid? UserId { get; set; }
    public string? BillingMode { get; set; }
    public string? Status { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderInvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Currency { get; set; }
    public decimal AmountDue { get; set; }
    public decimal AmountPaid { get; set; }
    public DateTimeOffset? DueUtc { get; set; }
    public DateTimeOffset? PaidUtc { get; set; }
    public string? HostedInvoiceUrl { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class UpdateBillingInvoiceRequest
{
    public string? BillingMode { get; set; }
    public string? Status { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderInvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Currency { get; set; }
    public decimal? AmountDue { get; set; }
    public decimal? AmountPaid { get; set; }
    public DateTimeOffset? DueUtc { get; set; }
    public DateTimeOffset? PaidUtc { get; set; }
    public string? HostedInvoiceUrl { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
