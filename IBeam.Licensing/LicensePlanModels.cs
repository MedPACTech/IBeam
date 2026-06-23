namespace IBeam.Licensing;

public sealed record LicensePlanInfo(
    string Key,
    string DisplayName,
    string? Description,
    IReadOnlyList<string> Entitlements,
    IReadOnlyDictionary<string, int> Limits,
    IReadOnlyDictionary<string, string> Metadata,
    bool IsConfigured = true);
