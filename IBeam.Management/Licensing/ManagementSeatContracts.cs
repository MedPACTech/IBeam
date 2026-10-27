namespace IBeam.Management.Licensing;

public static class ManagementSeatOperations
{
    public const string Read = "management.seats.read";
    public const string Assign = "management.seats.assign";
    public const string Unassign = "management.seats.unassign";
}
public sealed record ManagementSeatReadRequest;
public sealed record ManagementSeatAssignRequest(Guid LicenseId, Guid UserId, string ExpectedVersion);
public sealed record ManagementSeatUnassignRequest(Guid LicenseId, Guid UserId, Guid AssignmentId, string ExpectedVersion);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementSeatAssignAction(Guid UserId);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementSeatUnassignAction(Guid UserId, Guid AssignmentId);
/// <summary>Non-user/native opaque subjects have no public identifier or display text beyond the assignment GUID.</summary>
public sealed record ManagementSeatAssignment(Guid AssignmentId, Guid? UserId);
public sealed record ManagementSeatStatus(Guid TenantId, Guid LicenseId, string Version, int? SeatLimit,
    int AssignedCount, ManagementSeatAssignment[] Assignments);
public sealed record ManagementSeatTransitionResult(Guid TenantId, Guid LicenseId, Guid OperationId,
    string Outcome, Guid? UserId, Guid? AssignmentId, string? Disposition, string? Version);
public interface IManagementSeatPortalService
{
    Task<ManagementSeatStatus> ReadAsync(Guid applicationId, Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<ManagementSeatTransitionResult> AssignAsync(Guid applicationId, Guid tenantId, ManagementSeatAssignRequest request, string key, CancellationToken ct = default);
    Task<ManagementSeatTransitionResult> UnassignAsync(Guid applicationId, Guid tenantId, ManagementSeatUnassignRequest request, string key, CancellationToken ct = default);
    Task<ManagementSeatTransitionResult> FindAsync(Guid applicationId, Guid tenantId, Guid licenseId, Guid operationId, string operation, CancellationToken ct = default);
}
