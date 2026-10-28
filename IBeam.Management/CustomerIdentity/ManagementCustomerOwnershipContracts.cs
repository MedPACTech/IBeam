using System.Text.Json.Serialization;
namespace IBeam.Management.CustomerIdentity;

public sealed record ManagementCustomerOwnerDto(Guid UserId, string MembershipVersion);
public sealed record ManagementCustomerOwnershipDto(Guid TenantId, string Version, IReadOnlyList<ManagementCustomerOwnerDto> Owners);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TransferManagementCustomerOwnership
{
    public required Guid SourceUserId { get; init; }
    public required Guid DestinationUserId { get; init; }
    public required string SourceVersion { get; init; }
    public required string DestinationVersion { get; init; }
    public override string ToString() => "Ownership transfer (preconditions redacted)";
    public bool IsValid() => SourceUserId != Guid.Empty && DestinationUserId != Guid.Empty && SourceUserId != DestinationUserId &&
        Exact(SourceVersion) && Exact(DestinationVersion);
    public static bool Exact(string? version) => !string.IsNullOrWhiteSpace(version) && version != "*" && version.Length <= 256 && !version.Any(char.IsControl);
}
public sealed record ManagementCustomerOwnershipResult(ManagementIdentityOperationDto Operation);
public sealed record ManagementPortalCustomerOwnershipResult(ManagementPortalOperationResult Tracking);
public interface IManagementCustomerOwnershipService
{
    int ProtocolVersion { get; }
    Task<ManagementCustomerOwnershipDto> ReadAsync(Guid tenantId, CancellationToken ct = default);
    Task<ManagementCustomerOwnershipResult> TransferAsync(Guid tenantId, TransferManagementCustomerOwnership request, string version, string key, CancellationToken ct = default);
}
public interface IManagementPortalCustomerOwnershipService
{
    Task<ManagementCustomerOwnershipDto> ReadAsync(Guid applicationId, Guid tenantId, CancellationToken ct = default);
    Task<ManagementPortalCustomerOwnershipResult> TransferAsync(Guid applicationId, Guid tenantId, TransferManagementCustomerOwnership request, string version, string key, CancellationToken ct = default);
}
