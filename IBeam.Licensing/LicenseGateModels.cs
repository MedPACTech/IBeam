namespace IBeam.Licensing;

public sealed class LicenseGateRequest
{
    public Guid TenantId { get; set; }
    public LicenseSubject Subject { get; set; } = new(LicenseSubjectTypes.User, string.Empty);
    public string Entitlement { get; set; } = string.Empty;
    public string? OperationName { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed record LicenseGateResult(
    bool Allowed,
    Guid TenantId,
    LicenseSubject Subject,
    string Entitlement,
    string? OperationName,
    Guid? LicenseId,
    string? DenialCode,
    string? Reason,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static LicenseGateResult Allow(
        Guid tenantId,
        LicenseSubject subject,
        string entitlement,
        string? operationName,
        Guid licenseId,
        IReadOnlyDictionary<string, string> metadata)
        => new(true, tenantId, subject, entitlement, operationName, licenseId, null, null, metadata);

    public static LicenseGateResult Deny(
        Guid tenantId,
        LicenseSubject subject,
        string entitlement,
        string? operationName,
        string denialCode,
        string reason,
        IReadOnlyDictionary<string, string> metadata)
        => new(false, tenantId, subject, entitlement, operationName, null, denialCode, reason, metadata);
}

public static class LicenseGateDenialCodes
{
    public const string NoLicense = "no-license";
    public const string InactiveLicense = "inactive-license";
    public const string MissingEntitlement = "missing-entitlement";
    public const string MissingSeat = "missing-seat";
}
