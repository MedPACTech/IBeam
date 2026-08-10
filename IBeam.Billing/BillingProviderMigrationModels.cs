namespace IBeam.Billing;

public sealed record BillingSubscriptionProviderBindingInfo(
    Guid BindingId,
    Guid TenantId,
    Guid BillingSubscriptionId,
    string ProviderName,
    string? ProviderCustomerId,
    string ProviderSubscriptionId,
    string? ProviderPriceId,
    bool IsActive,
    DateTimeOffset ActivatedUtc,
    DateTimeOffset? RetiredUtc,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record BillingProviderMigrationRecord(
    Guid MigrationId,
    Guid TenantId,
    Guid BillingSubscriptionId,
    string IdempotencyKey,
    string SourceProviderName,
    string TargetProviderName,
    Guid LicenseKey,
    Guid TargetBindingId,
    DateTimeOffset CompletedUtc);

public sealed class MigrateBillingProviderRequest
{
    public Guid BillingSubscriptionId { get; set; }
    public string TargetProviderName { get; set; } = string.Empty;
    public string? TargetProviderCustomerId { get; set; }
    public string TargetProviderSubscriptionId { get; set; } = string.Empty;
    public BillingPriceReferenceInfo? TargetPrice { get; set; }
    public string TargetSubscriptionStatus { get; set; } = BillingSubscriptionStatuses.Active;
    public string? TargetProviderStatus { get; set; }
    public int? SeatQuantity { get; set; }
    public DateTimeOffset? CurrentPeriodStartsUtc { get; set; }
    public DateTimeOffset? CurrentPeriodEndsUtc { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed record BillingProviderMigrationInfo(
    Guid MigrationId,
    BillingSubscriptionInfo Subscription,
    Guid LicenseKey,
    IReadOnlyList<BillingSubscriptionProviderBindingInfo> ProviderBindings,
    bool WasReplay);
