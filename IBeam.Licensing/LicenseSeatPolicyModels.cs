namespace IBeam.Licensing;

public sealed class GrantSingleUserLicenseRequest
{
    public GrantTenantLicenseRequest License { get; set; } = new();
    public LicenseSubject Subject { get; set; } = new(LicenseSubjectTypes.User, string.Empty);
    public Dictionary<string, string> SeatMetadata { get; set; } = [];
}

public sealed class GrantTenantSeatLicenseRequest
{
    public GrantTenantLicenseRequest License { get; set; } = new();
    public int? SeatLimit { get; set; }
    public List<LicenseSubject> InitialSubjects { get; set; } = [];
    public Dictionary<string, string> SeatMetadata { get; set; } = [];
}

public sealed record TenantLicenseWithSeatsInfo(
    TenantLicenseInfo License,
    IReadOnlyList<LicenseSeatAssignmentInfo> Assignments);
