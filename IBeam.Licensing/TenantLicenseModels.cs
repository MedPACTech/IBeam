namespace IBeam.Licensing;

public sealed record TenantLicenseRecord(
    Guid LicenseId,
    Guid TenantId,
    string PlanKey,
    string DisplayName,
    string Status,
    IReadOnlyList<string> Entitlements,
    IReadOnlyDictionary<string, int> Limits,
    int? SeatLimit,
    DateTimeOffset StartsUtc,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? RevokedUtc,
    string? RevocationReason,
    string? ProviderName,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    string? ProviderPriceId,
    string? ProviderStatus,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool IsActive(DateTimeOffset now)
        => string.Equals(Status, LicenseStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
           RevokedUtc is null &&
           StartsUtc <= now &&
           (ExpiresUtc is null || ExpiresUtc > now);
}

public sealed record TenantLicenseInfo(
    Guid LicenseId,
    Guid TenantId,
    string PlanKey,
    string DisplayName,
    string Status,
    IReadOnlyList<string> Entitlements,
    IReadOnlyDictionary<string, int> Limits,
    int? SeatLimit,
    DateTimeOffset StartsUtc,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? RevokedUtc,
    string? RevocationReason,
    string? ProviderName,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    string? ProviderPriceId,
    string? ProviderStatus,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static TenantLicenseInfo FromRecord(TenantLicenseRecord record)
        => new(
            record.LicenseId,
            record.TenantId,
            record.PlanKey,
            record.DisplayName,
            record.Status,
            record.Entitlements,
            record.Limits,
            record.SeatLimit,
            record.StartsUtc,
            record.ExpiresUtc,
            record.CreatedUtc,
            record.CreatedByUserId,
            record.RevokedUtc,
            record.RevocationReason,
            record.ProviderName,
            record.ProviderCustomerId,
            record.ProviderSubscriptionId,
            record.ProviderPriceId,
            record.ProviderStatus,
            record.Metadata);
}

public sealed class GrantTenantLicenseRequest
{
    public string PlanKey { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Status { get; set; }
    public List<string> Entitlements { get; set; } = [];
    public Dictionary<string, int> Limits { get; set; } = [];
    public int? SeatLimit { get; set; }
    public DateTimeOffset? StartsUtc { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderCustomerId { get; set; }
    public string? ProviderSubscriptionId { get; set; }
    public string? ProviderPriceId { get; set; }
    public string? ProviderStatus { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class UpdateTenantLicenseRequest
{
    public string? DisplayName { get; set; }
    public string? Status { get; set; }
    public List<string>? Entitlements { get; set; }
    public Dictionary<string, int>? Limits { get; set; }
    public int? SeatLimit { get; set; }
    public DateTimeOffset? StartsUtc { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderCustomerId { get; set; }
    public string? ProviderSubscriptionId { get; set; }
    public string? ProviderPriceId { get; set; }
    public string? ProviderStatus { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class RevokeTenantLicenseRequest
{
    public string? Reason { get; set; }
}
