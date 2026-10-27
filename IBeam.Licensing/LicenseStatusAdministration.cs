namespace IBeam.Licensing;

/// <summary>Trusted native administration command, not customer authentication or a new entitlement grant.</summary>
public sealed record LicenseStatusCommand(Guid TenantId, Guid LicenseId, Guid OperationId, string AuthorityScope,
    string ExpectedVersion, string Transition);
public sealed record LicenseStatusSnapshot(TenantLicenseRecord License, string Version, string? ResumeStatus);
public sealed record LicenseStatusReceipt(Guid TenantId, Guid LicenseId, Guid OperationId, string Transition, string Status, string Version);
public sealed record LicenseStatusCommit(LicenseStatusCommand Command, string IntentHash, string Status, string? ResumeStatus);
public static class LicenseStatusFingerprint
{
    public static string Compute(LicenseStatusCommand command) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(command)));
}

/// <summary>Optional provider protocol. Version compare, status/resume state and receipt commit atomically.
/// All native writers must participate; a read or receipt lookup must never create a guard or write state.</summary>
public interface ILicenseStatusConditionalStore
{
    int StatusProtocolVersion { get; }
    bool NativeWritersParticipate { get; }
    bool DurableStatusReceipts { get; }
    Task<LicenseStatusSnapshot?> ReadStatusAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<LicenseStatusReceipt?> FindStatusReceiptAsync(Guid tenantId, Guid licenseId, Guid operationId,
        string authorityScope, string transition, CancellationToken ct = default);
    Task<LicenseStatusReceipt> CommitStatusAsync(LicenseStatusCommit commit, CancellationToken ct = default);
}

/// <summary>Target-owned restoration rule; receives native state, never a portal-selected replacement status.</summary>
public interface ILicenseStatusRestorePolicy
{
    Task RequireAsync(LicenseStatusCommand command, TenantLicenseRecord current, TenantLicenseRecord candidate, CancellationToken ct = default);
}
public interface ITenantLicenseStatusAdministrationService
{
    Task<LicenseStatusSnapshot> ReadAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default);
    Task<LicenseStatusReceipt> ChangeAsync(LicenseStatusCommand command, CancellationToken ct = default);
    Task<LicenseStatusReceipt?> FindAsync(Guid tenantId, Guid licenseId, Guid operationId, string authorityScope, string transition, CancellationToken ct = default);
}

public sealed class LicenseStatusPreconditionException() : LicensingException("The licence version changed.");
public sealed class LicenseStatusConflictException() : LicensingException("The licence operation conflicts with current state.");
public sealed class LicenseStatusUnavailableException() : LicensingException("Conditional licence administration is unavailable.");
