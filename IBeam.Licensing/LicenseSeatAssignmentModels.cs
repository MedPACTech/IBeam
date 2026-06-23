namespace IBeam.Licensing;

public sealed record LicenseSeatAssignmentInfo(
    Guid AssignmentId,
    Guid TenantId,
    Guid LicenseId,
    LicenseSubject Subject,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class AssignLicenseSeatRequest
{
    public LicenseSubject Subject { get; set; } = new(LicenseSubjectTypes.User, string.Empty);
    public Dictionary<string, string> Metadata { get; set; } = [];
}
