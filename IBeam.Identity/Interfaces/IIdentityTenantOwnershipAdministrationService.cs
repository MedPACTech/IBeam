using IBeam.Identity.Models;
namespace IBeam.Identity.Interfaces;

/// <summary>Trusted native adapter. Calling services own caller authorization and metadata-only audit.</summary>
public interface IIdentityTenantOwnershipAdministrationService
{
    Task<IdentityTenantOwnerSnapshot> ReadOwnersAsync(Guid tenantId, CancellationToken ct = default);
    Task<IdentityTenantOwnershipTransferResult> TransferAsync(IdentityTenantOwnershipTransferRequest request, CancellationToken ct = default);
}

/// <summary>Optional provider protocol: both membership changes, the shared fence and an immutable receipt commit atomically.</summary>
public interface IIdentityTenantOwnershipTransferStore : IIdentityTenantOwnershipConditionalStore
{
    /// <summary>Never creates a guard or rewrites membership/legacy tenant data.</summary>
    Task<IdentityTenantMembershipSnapshot> ReadOwnershipSnapshotAsync(Guid tenantId, int maximumMembers, CancellationToken ct = default);
    Task<IdentityTenantOwnershipTransferReceipt?> FindOwnershipTransferAsync(Guid tenantId, Guid operationId, CancellationToken ct = default);
    Task<IdentityTenantOwnershipTransferReceipt> CommitOwnershipTransferAsync(IdentityTenantOwnershipChange change, CancellationToken ct = default);
}
