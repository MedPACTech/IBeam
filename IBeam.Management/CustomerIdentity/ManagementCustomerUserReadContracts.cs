using System.Text.Json.Serialization;
namespace IBeam.Management.CustomerIdentity;

public sealed record ManagementCustomerUserReadEntity(Guid TenantId, Guid UserId, string? Email, string? Status,
    string? RolesCsv, string? RoleIdsCsv, DateTimeOffset? CreatedUtc, DateTimeOffset? DisabledUtc, string Version)
{
    public DateTimeOffset? ModifiedUtc { get; init; }
    public override string ToString() => "Customer membership metadata (identity fields redacted)";
}
public sealed record ManagementCustomerInvitationReadEntity(Guid TenantId, Guid InviteId, string? DestinationType, string? Destination,
    string? FirstName, string? LastName, string? Status, string? RoleIdsCsv, string? RoleNamesJson,
    DateTimeOffset? CreatedUtc, DateTimeOffset? ExpiresUtc, DateTimeOffset? SentUtc, DateTimeOffset? RedeemedUtc, DateTimeOffset? RevokedUtc, string Version)
{
    public DateTimeOffset? ModifiedUtc { get; init; }
    public override string ToString() => "Customer invitation metadata (identity fields redacted)";
}
public sealed record ManagementCustomerReadPage<T>(IReadOnlyList<T> Items, [property: JsonIgnore] string? ContinuationToken)
{
    public override string ToString() => "Customer provider page (state redacted)";
}
public interface IManagementCustomerUserReadRepository
{
    string SourceId { get; }
    Task<ManagementCustomerReadPage<ManagementCustomerUserReadEntity>> ReadPageAsync(Guid tenantId, int pageSize, string? continuationToken, CancellationToken ct = default);
    Task<ManagementCustomerUserReadEntity?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
public interface IManagementCustomerInvitationReadRepository
{
    string SourceId { get; }
    Task<ManagementCustomerReadPage<ManagementCustomerInvitationReadEntity>> ReadPageAsync(Guid tenantId, int pageSize, string? continuationToken, CancellationToken ct = default);
    Task<ManagementCustomerInvitationReadEntity?> FindAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default);
}
public interface IManagementCustomerTenantScopeService
{
    string SourceId { get; }
    Task<ManagementDelegatedActor> RequireAsync(Guid tenantId, string operation, CancellationToken ct = default);
}
public interface IManagementCustomerUserDirectoryService : IManagementCustomerUserDirectoryQuery;
public sealed record ManagementCustomerInvitationDto(Guid TenantId, Guid InviteId, string? Email, string? FirstName, string? LastName,
    string Status, IReadOnlyList<Guid> RoleIds, IReadOnlyList<string> RoleNames, DateTimeOffset? CreatedUtc, DateTimeOffset? ExpiresUtc,
    DateTimeOffset? UpdatedUtc, string Version)
{
    public override string ToString() => "Customer invitation (identity fields redacted)";
}
public interface IManagementCustomerInvitationDirectoryService
{
    Task<ManagementCustomerDirectoryPage<ManagementCustomerInvitationDto>> ListAsync(Guid tenantId, ManagementCustomerDirectoryQuery query, CancellationToken ct = default);
    Task<ManagementCustomerInvitationDto?> FindAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default);
}
