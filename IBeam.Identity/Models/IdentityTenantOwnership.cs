using IBeam.Identity.Interfaces;
namespace IBeam.Identity.Models;

public enum IdentityTenantOwnerMode { Unspecified = 0, SingleOwner = 1, MultipleOwners = 2 }
public sealed record IdentityTenantOwner(Guid UserId, string MembershipVersion);
public sealed record IdentityTenantOwnerSnapshot(Guid TenantId, string Version, IReadOnlyList<IdentityTenantOwner> Owners);
public sealed record IdentityTenantOwnershipTransferRequest(Guid TenantId, Guid SourceUserId, Guid DestinationUserId,
    string SourceVersion, string DestinationVersion, string OwnershipVersion, Guid OperationId);
public sealed record IdentityTenantOwnershipTransferResult(Guid TenantId, Guid OperationId, Guid SourceUserId, Guid DestinationUserId);

/// <summary>Private provider payload; never bind this from an HTTP request.</summary>
public sealed record IdentityTenantOwnershipChange(IdentityTenantMembershipState Source, IdentityTenantMembershipState Destination,
    string ExpectedOwnershipVersion, Guid OperationId, string IntentHash);
/// <summary>Durable metadata-only receipt, retained even when later operations change membership versions.</summary>
public sealed record IdentityTenantOwnershipTransferReceipt(Guid TenantId, Guid OperationId, Guid SourceUserId, Guid DestinationUserId, string IntentHash);
