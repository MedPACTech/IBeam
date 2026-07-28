using IBeam.Credits;
using IBeam.Licensing;

namespace IBeam.Licensing.Credits;

public sealed class LicenseCreditGateRequest
{
    public Guid TenantId { get; set; }
    public LicenseSubject Subject { get; set; } = new(LicenseSubjectTypes.User, string.Empty);
    public string Entitlement { get; set; } = string.Empty;
    public string? OperationName { get; set; }
    public Guid CreditAccountId { get; set; }
    public string? CreditBucketKey { get; set; }
    public decimal EstimatedCredits { get; set; }
    public decimal? MaxCredits { get; set; }
    public string CreditPolicyMode { get; set; } = CreditPolicyModes.StrictPrepaid;
    public bool AllowOverage { get; set; }
    public DateTimeOffset? ReservationExpiresUtc { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];

    public bool RequiresCredits
        => CreditAccountId != Guid.Empty && !string.IsNullOrWhiteSpace(CreditBucketKey);
}

public sealed record LicenseCreditGateResult(
    bool Allowed,
    string? DenialScope,
    string? DenialCode,
    string? Reason,
    LicenseGateResult License,
    CreditOperationDecision? Credit)
{
    public static LicenseCreditGateResult Allow(LicenseGateResult license, CreditOperationDecision? credit)
        => new(true, null, null, null, license, credit);

    public static LicenseCreditGateResult DenyLicense(LicenseGateResult license)
        => new(false, LicenseCreditGateDenialScopes.License, license.DenialCode, license.Reason, license, null);

    public static LicenseCreditGateResult DenyCredit(LicenseGateResult license, CreditOperationDecision credit)
        => new(false, LicenseCreditGateDenialScopes.Credit, credit.DenialReason, credit.Message, license, credit);
}

public sealed record CreditMeasuredOperationResult<T>(
    T Value,
    decimal ActualCredits,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record LicenseCreditExecutionResult<T>(
    bool Allowed,
    bool OperationExecuted,
    T? Value,
    LicenseCreditGateResult Gate,
    CreditOperationSettlementResult? CreditSettlement);

public static class LicenseCreditGateDenialScopes
{
    public const string License = "license";
    public const string Credit = "credit";
}
