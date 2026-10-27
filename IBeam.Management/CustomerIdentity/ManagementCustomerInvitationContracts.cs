using System.Text.Json.Serialization;
namespace IBeam.Management.CustomerIdentity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeManagementCustomerInvitation;
public sealed record ManagementCustomerInvitationResult(ManagementIdentityOperationDto Operation, string Status, ManagementCustomerInvitationDto? Invitation);
public interface IManagementCustomerInvitationService
{
    Task<ManagementCustomerInvitationResult> ResendAsync(Guid tenantId, Guid inviteId, string key, string version, CancellationToken ct = default);
    Task<ManagementCustomerInvitationResult> RevokeAsync(Guid tenantId, Guid inviteId, string key, string version, CancellationToken ct = default);
}
/// <summary>Private per-transition recovery journal, physically separate from public receipts and native invitations.</summary>
public sealed record ManagementCustomerInvitationTransitionEntity
{
    public required ManagementTenantAdministrationScope Scope { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid InviteId { get; init; }
    public required Guid OperationId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required Guid UserId { get; init; }
    public required string Kind { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required DateTimeOffset ExpiresUtc { get; init; }
    public string Stage { get; init; } = "reserved";
    public string DeliveryStatus { get; init; } = "pending";
    public DateTimeOffset? DeliveryLeaseUtc { get; init; }
    [JsonIgnore] public string EncryptedInput { get; init; } = "";
    public string Version { get; init; } = "";
    public override string ToString() => "Invitation transition journal (private recovery input redacted)";
}
public interface IManagementCustomerInvitationTransitionRepository
{
    Task<ManagementCustomerInvitationTransitionEntity?> GetAsync(ManagementTenantAdministrationScope scope, Guid tenantId, Guid operationId, CancellationToken ct = default);
    Task<bool> TryCreateAsync(ManagementCustomerInvitationTransitionEntity entity, CancellationToken ct = default);
    Task<ManagementCustomerInvitationTransitionEntity> ReplaceAsync(ManagementCustomerInvitationTransitionEntity entity, string expectedVersion, CancellationToken ct = default);
}
