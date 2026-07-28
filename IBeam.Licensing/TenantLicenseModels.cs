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
    IReadOnlyDictionary<string, string> Metadata,
    string CommercialStatus = LicenseCommercialStatuses.Unknown,
    DateTimeOffset? GraceEndsUtc = null)
{
    public bool IsActive(DateTimeOffset now)
        => EvaluateRuntimeEligibility(now).IsEligible;

    public LicenseRuntimeEligibilityInfo EvaluateRuntimeEligibility(DateTimeOffset now)
    {
        var status = Status.Trim();

        if (RevokedUtc is not null || string.Equals(status, LicenseStatuses.Revoked, StringComparison.OrdinalIgnoreCase))
            return LicenseRuntimeEligibilityInfo.Deny(LicenseRuntimeStatuses.Revoked, "License was revoked.");

        if (StartsUtc > now)
            return LicenseRuntimeEligibilityInfo.Deny(LicenseRuntimeStatuses.NotStarted, "License has not started.");

        if (string.Equals(status, LicenseStatuses.Suspended, StringComparison.OrdinalIgnoreCase))
            return LicenseRuntimeEligibilityInfo.Deny(LicenseRuntimeStatuses.Suspended, "License is suspended.");

        if (string.Equals(status, LicenseStatuses.Expired, StringComparison.OrdinalIgnoreCase))
            return LicenseRuntimeEligibilityInfo.Deny(LicenseRuntimeStatuses.Expired, "License is expired.");

        var pastExpiration = ExpiresUtc is { } expires && expires <= now;
        if (pastExpiration)
        {
            if (string.Equals(status, LicenseStatuses.Grace, StringComparison.OrdinalIgnoreCase) &&
                GraceEndsUtc is { } graceEnds &&
                graceEnds > now)
            {
                return LicenseRuntimeEligibilityInfo.Allow(LicenseRuntimeStatuses.Grace);
            }

            return LicenseRuntimeEligibilityInfo.Deny(LicenseRuntimeStatuses.Expired, "License is expired.");
        }

        if (string.Equals(status, LicenseStatuses.Active, StringComparison.OrdinalIgnoreCase))
            return LicenseRuntimeEligibilityInfo.Allow(LicenseRuntimeStatuses.Active);

        if (string.Equals(status, LicenseStatuses.Trialing, StringComparison.OrdinalIgnoreCase))
            return LicenseRuntimeEligibilityInfo.Allow(LicenseRuntimeStatuses.Trialing);

        if (string.Equals(status, LicenseStatuses.Grace, StringComparison.OrdinalIgnoreCase))
            return GraceEndsUtc is null || GraceEndsUtc > now
                ? LicenseRuntimeEligibilityInfo.Allow(LicenseRuntimeStatuses.Grace)
                : LicenseRuntimeEligibilityInfo.Deny(LicenseRuntimeStatuses.Expired, "License grace period has ended.");

        if (string.Equals(status, LicenseStatuses.Manual, StringComparison.OrdinalIgnoreCase))
            return LicenseRuntimeEligibilityInfo.Allow(LicenseRuntimeStatuses.Manual);

        return LicenseRuntimeEligibilityInfo.Deny(LicenseRuntimeStatuses.Unknown, $"License status '{Status}' is not runtime eligible.");
    }
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
    IReadOnlyDictionary<string, string> Metadata,
    string CommercialStatus = LicenseCommercialStatuses.Unknown,
    DateTimeOffset? GraceEndsUtc = null)
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
            record.Metadata,
            record.CommercialStatus,
            record.GraceEndsUtc);
}

public sealed record LicenseRuntimeEligibilityInfo(
    bool IsEligible,
    string RuntimeStatus,
    string? Reason)
{
    public static LicenseRuntimeEligibilityInfo Allow(string runtimeStatus)
        => new(true, runtimeStatus, null);

    public static LicenseRuntimeEligibilityInfo Deny(string runtimeStatus, string reason)
        => new(false, runtimeStatus, reason);
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
    public string? CommercialStatus { get; set; }
    public DateTimeOffset? GraceEndsUtc { get; set; }
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
    public string? CommercialStatus { get; set; }
    public DateTimeOffset? GraceEndsUtc { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class RevokeTenantLicenseRequest
{
    public string? Reason { get; set; }
}
