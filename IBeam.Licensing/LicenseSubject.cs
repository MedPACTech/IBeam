namespace IBeam.Licensing;

public sealed record LicenseSubject(
    string SubjectType,
    string SubjectId,
    string? DisplayName = null);
