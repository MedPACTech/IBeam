namespace IBeam.Licensing;

public sealed record LicenseSeatSnapshot(TenantLicenseRecord License, string Version,
    IReadOnlyList<LicenseSeatAssignmentInfo> Assignments, bool RequiresProtection);
public sealed record LicenseSeatCommand(Guid TenantId, Guid LicenseId, Guid UserId, Guid? AssignmentId,
    Guid OperationId, string AuthorityScope, string ExpectedVersion, string Transition);
public sealed record LicenseSeatReceipt(Guid TenantId, Guid LicenseId, Guid UserId, Guid AssignmentId, Guid OperationId,
    string Transition, string Disposition, string Version);
public sealed record LicenseSeatCommit(LicenseSeatCommand Command, string IntentHash,
    LicenseSeatAssignmentInfo? Assignment, Guid? RemoveAssignmentId, string Disposition);

/// <summary>One native licence aggregate revision fences all seat and licence writers.
/// Conditional mutation and immutable receipt commit atomically; snapshots/lookups never initialize guards.</summary>
public interface ILicenseSeatConditionalStore
{
    int SeatProtocolVersion { get; }
    bool NativeSeatWritersParticipate { get; }
    bool DurableSeatReceipts { get; }
    Task<bool> RequiresSeatProtectionAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<LicenseSeatSnapshot?> ReadSeatsAsync(Guid tenantId, Guid licenseId, int maximumAssignments, CancellationToken ct = default);
    Task<LicenseSeatReceipt?> FindSeatReceiptAsync(Guid tenantId, Guid licenseId, Guid operationId,
        string authorityScope, string transition, CancellationToken ct = default);
    Task<LicenseSeatReceipt> CommitSeatAsync(LicenseSeatCommit commit, CancellationToken ct = default);
    /// <summary>Existing authorized native writers use the same exact revision; no Management receipt or authority is inferred.</summary>
    Task ApplyNativeSeatAsync(Guid tenantId, Guid licenseId, string expectedVersion, LicenseSeatAssignmentInfo? assignment,
        Guid? removeAssignmentId, CancellationToken ct = default);
}
/// <summary>Native host-owned customer membership/subject policy. Assignment requires a current eligible user in this tenant.
/// Revocation can explicitly allow disabled/deleted users for cleanup; it must still verify native tenant scope.</summary>
public interface ILicenseSeatSubjectPolicy
{
    Task RequireAsync(Guid tenantId, Guid userId, string transition, CancellationToken ct = default);
}
public interface ITenantLicenseSeatAdministrationService
{
    Task<LicenseSeatSnapshot> ReadAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<LicenseSeatReceipt> ChangeAsync(LicenseSeatCommand command, CancellationToken ct = default);
    Task<LicenseSeatReceipt?> FindAsync(Guid tenantId, Guid licenseId, Guid operationId, string scope, string transition, CancellationToken ct = default);
}
public static class LicenseSeatFingerprint
{
    public static string Compute(LicenseSeatCommand command) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(command)));
    public static Guid AssignmentId(LicenseSeatCommand command) => new(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(command.AuthorityScope + "\n" + command.TenantId.ToString("N") + "\n" + command.LicenseId.ToString("N") + "\n" + command.OperationId.ToString("N"))).AsSpan(0, 16));
    public static bool MatchesUser(LicenseSeatAssignmentInfo assignment, Guid userId) =>
        string.Equals(assignment.Subject.SubjectType, LicenseSubjectTypes.User, StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParseExact(assignment.Subject.SubjectId, "D", out var id) && id == userId;
}
public static class LicenseSeatProtocol { public const int MaximumAssignments = 2000; }
public sealed class LicenseSeatPreconditionException() : LicensingException("The licence or seat version changed.");
public sealed class LicenseSeatConflictException() : LicensingException("The seat operation conflicts with current state.");
public sealed class LicenseSeatCapacityException() : LicensingException("The licence has no available seat capacity.");
public sealed class LicenseSeatUnavailableException() : LicensingException("Conditional seat administration is unavailable.");
