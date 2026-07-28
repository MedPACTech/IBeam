using IBeam.Licensing;

namespace IBeam.Credits.Api;

public sealed class GetCreditBootstrapRequest
{
    public GetLicenseRuntimeContextRequest License { get; set; } = new();
    public GetCreditRuntimeSummaryRequest? Credits { get; set; }
}

public sealed record CreditBootstrapInfo(
    LicenseRuntimeContextInfo License,
    CreditRuntimeSummaryInfo? Credits,
    bool CreditsAreGuidanceOnly);
