namespace IBeam.Licensing;

public sealed record LicenseAuthorizationResult(
    bool Allowed,
    string? Reason = null,
    Guid? LicenseId = null)
{
    public static LicenseAuthorizationResult Allow(Guid licenseId)
        => new(true, LicenseId: licenseId);

    public static LicenseAuthorizationResult Deny(string reason)
        => new(false, reason);
}

public sealed class CheckLicenseEntitlementRequest
{
    public LicenseSubject Subject { get; set; } = new(LicenseSubjectTypes.User, string.Empty);
    public string Entitlement { get; set; } = string.Empty;
}
