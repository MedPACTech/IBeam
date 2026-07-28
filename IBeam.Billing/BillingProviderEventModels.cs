namespace IBeam.Billing;

public sealed record BillingProviderEventRecord(
    Guid BillingProviderEventId,
    string ProviderName,
    string ProviderEventId,
    string EventType,
    string Status,
    DateTimeOffset ReceivedUtc,
    DateTimeOffset? ProcessedUtc,
    Guid? TenantId,
    Guid? UserId,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    string? ProviderInvoiceId,
    string? PayloadContentType,
    string? PayloadReference,
    IReadOnlyDictionary<string, string> Metadata)
{
    public string IdempotencyKey => BillingProviderEventInfo.CreateIdempotencyKey(ProviderName, ProviderEventId);

    public BillingProviderEventInfo ToInfo()
        => BillingProviderEventInfo.FromRecord(this);
}

public sealed record BillingProviderEventInfo(
    Guid BillingProviderEventId,
    string ProviderName,
    string ProviderEventId,
    string EventType,
    string Status,
    DateTimeOffset ReceivedUtc,
    DateTimeOffset? ProcessedUtc,
    Guid? TenantId,
    Guid? UserId,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    string? ProviderInvoiceId,
    string? PayloadContentType,
    string? PayloadReference,
    IReadOnlyDictionary<string, string> Metadata)
{
    public string IdempotencyKey => CreateIdempotencyKey(ProviderName, ProviderEventId);

    public static BillingProviderEventInfo FromRecord(BillingProviderEventRecord record)
        => new(
            record.BillingProviderEventId,
            BillingPriceReferenceInfo.NormalizeRequired(record.ProviderName, nameof(record.ProviderName)),
            BillingPriceReferenceInfo.NormalizeRequired(record.ProviderEventId, nameof(record.ProviderEventId)),
            BillingPriceReferenceInfo.NormalizeRequired(record.EventType, nameof(record.EventType)),
            BillingProviderEventStatuses.Normalize(record.Status),
            record.ReceivedUtc,
            record.ProcessedUtc,
            record.TenantId,
            record.UserId,
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderCustomerId),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderSubscriptionId),
            BillingPriceReferenceInfo.NormalizeOptional(record.ProviderInvoiceId),
            BillingPriceReferenceInfo.NormalizeOptional(record.PayloadContentType),
            BillingPriceReferenceInfo.NormalizeOptional(record.PayloadReference),
            BillingPriceReferenceInfo.NormalizeMetadata(record.Metadata));

    public static string CreateIdempotencyKey(string providerName, string providerEventId)
        => $"{BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName)).ToLowerInvariant()}:{BillingPriceReferenceInfo.NormalizeRequired(providerEventId, nameof(providerEventId))}";
}

public sealed class RecordBillingProviderEventRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? Status { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string? ProviderCustomerId { get; set; }
    public string? ProviderSubscriptionId { get; set; }
    public string? ProviderInvoiceId { get; set; }
    public string? PayloadContentType { get; set; }
    public string? PayloadReference { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}
