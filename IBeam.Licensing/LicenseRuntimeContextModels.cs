namespace IBeam.Licensing;

public sealed class GetLicenseRuntimeContextRequest
{
    public LicenseSubject Subject { get; set; } = new(LicenseSubjectTypes.User, string.Empty);
    public bool IncludeAdminDetails { get; set; }
}

public sealed record LicenseRuntimeContextInfo(
    Guid TenantId,
    LicenseSubject Subject,
    string Status,
    bool IsLicensed,
    bool IsLimited,
    bool ContactAdmin,
    IReadOnlyList<string> Entitlements,
    IReadOnlyList<LicenseRuntimeSeatInfo> Seats,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? GraceEndsUtc,
    IReadOnlyList<LicenseCreditSummaryInfo> CreditSummaries,
    IReadOnlyList<LicenseRuntimeSummaryInfo> Licenses);

public sealed record LicenseRuntimeSeatInfo(
    Guid LicenseId,
    string PlanKey,
    string State,
    int? SeatLimit);

public sealed record LicenseRuntimeSummaryInfo(
    Guid LicenseId,
    string PlanKey,
    string DisplayName,
    string RuntimeStatus,
    string CommercialStatus,
    IReadOnlyList<string> Entitlements,
    int? SeatLimit,
    DateTimeOffset StartsUtc,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? GraceEndsUtc);

public sealed record LicenseCreditSummaryInfo(
    string BucketKey,
    decimal Available,
    decimal? Reserved,
    DateTimeOffset? ExpiresUtc);

public static class LicenseRuntimeContextStatuses
{
    public const string Active = "active";
    public const string Limited = "limited";
    public const string ContactAdmin = "contact-admin";
    public const string Expired = "expired";
    public const string Unlicensed = "unlicensed";
}

public static class LicenseRuntimeSeatStates
{
    public const string NotRequired = "not-required";
    public const string Assigned = "assigned";
    public const string Missing = "missing";
}
