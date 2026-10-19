using System.Text.Json.Serialization;
using IBeam.Management.Entities;
namespace IBeam.Management.CustomerIdentity;

public sealed record ManagementIdentityOperationScope(Guid IdentityRealmId, Guid OrganizationId, Guid ApplicationId);
/// <summary>Server-side request built by a validated business service. Never persist the key or payload.</summary>
public sealed class ManagementIdentityMutationRequest
{
    public required string Operation { get; init; }
    public Guid ResourceScope { get; init; }
    /// <summary>Set only for changing an existing resource. A tenant-scoped create still reserves a new resource ID.</summary>
    public Guid? ExistingResourceId { get; init; }
    [JsonIgnore] public string IdempotencyKey { get; init; } = "";
    [JsonIgnore] public ReadOnlyMemory<byte> CanonicalPayload { get; init; }
    public override string ToString() => "Management mutation request (payload and key redacted)";
}
public sealed record ManagementIdentityOperationDto(Guid OperationId, string Operation, string Status, int Step,
    Guid ResourceId, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, DateTimeOffset ExpiresUtc);
public sealed record ManagementIdentityMutationContext(Guid OperationId, Guid ResourceId, Guid ResourceScope, int Step, bool IsRecovery);
public enum ManagementIdentityReconciliation { Absent, Applied, Unknown }
/// <summary>Trusted business adapter: reconcile by stable ID/postcondition and apply with provider conditional writes.
/// Never implement an unguarded insert/update; a lease alone cannot fence a downstream store.</summary>
public interface IManagementIdentityMutationHandler
{
    Task<ManagementIdentityReconciliation> ReconcileAsync(ManagementIdentityMutationContext context, CancellationToken ct);
    Task ApplyAsync(ManagementIdentityMutationContext context, CancellationToken ct);
}
public interface IManagementIdentityOperationRepository
{
    Task<ManagementIdentityOperationEntity?> GetAsync(ManagementIdentityOperationScope scope, Guid operationId, CancellationToken ct = default);
    Task<bool> TryCreateAsync(ManagementIdentityOperationEntity operation, CancellationToken ct = default);
    Task<ManagementIdentityOperationEntity> ReplaceAsync(ManagementIdentityOperationEntity operation, string expectedVersion, CancellationToken ct = default);
}
/// <summary>Append once by AuditId; retry of identical metadata succeeds, conflicting metadata must fail.</summary>
public interface IManagementIdentityOperationAuditRepository
{
    Task AppendOnceAsync(ManagementAuditEntity entry, CancellationToken ct = default);
}
public interface IManagementIdentityOperationService
{
    Task<ManagementIdentityOperationDto> ExecuteAsync(ManagementIdentityMutationRequest request, IManagementIdentityMutationHandler handler, CancellationToken ct = default);
    Task<ManagementIdentityOperationDto> GetAsync(Guid operationId, string authority, CancellationToken ct = default);
    Task DeliverAuditAsync(Guid operationId, string authority, CancellationToken ct = default);
}
public interface IManagementPortalIdentityOperationService
{
    Task<ManagementIdentityOperationDto> GetAsync(Guid applicationId, Guid operationId, string authority, CancellationToken ct = default);
}
public sealed class ManagementIdentityOperationConflictException() : Exception("The management operation conflicts with an existing request.");
