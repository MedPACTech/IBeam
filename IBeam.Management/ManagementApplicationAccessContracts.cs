using IBeam.Management.Entities;
namespace IBeam.Management;

public static class ManagementAccessDelegation
{
    public const string Errors = "management.application-access.delegate.errors";
    public const string Logs = "management.application-access.delegate.logs";
}
public interface IManagementApplicationStateProvider
{
    Task<ManagementApplicationEntity?> GetAsync(Guid organizationId, Guid applicationId, CancellationToken ct = default);
}
public interface IManagementApplicationAccessRepository
{
    Task<ManagementApplicationAccessEntity?> GetAsync(Guid organizationId, Guid applicationId, Guid userId, CancellationToken ct = default);
    Task<ManagementApplicationAccessPage> ListAsync(Guid organizationId, Guid applicationId, Guid? afterUserId, int pageSize, CancellationToken ct = default);
    Task<ManagementApplicationAccessEntity> CreateAsync(ManagementApplicationAccessEntity row, CancellationToken ct = default);
    Task<ManagementApplicationAccessEntity> ReplaceAsync(ManagementApplicationAccessEntity row, string expectedVersion, CancellationToken ct = default);
}
public sealed record ManagementApplicationAccessPage(IReadOnlyList<ManagementApplicationAccessEntity> Items, Guid? Next);
public sealed record ManagementApplicationAccessDto(Guid ApplicationId, Guid UserId, IReadOnlyList<string> Permissions,
    bool IsRevoked, DateTimeOffset UpdatedUtc, Guid UpdatedBy, DateTimeOffset? RevokedUtc, Guid? RevokedBy, string Version);
public sealed record ManagementApplicationAccessList(IReadOnlyList<ManagementApplicationAccessDto> Items, int PageSize, string? ContinuationToken);
public sealed class ManagementApplicationAccessRequest
{
    public string[] Permissions { get; set; } = [];
    public string? ExpectedVersion { get; set; }
}
public interface IManagementApplicationAccessService
{
    Task<ManagementApplicationAccessList> ListAsync(Guid applicationId, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
    Task<ManagementApplicationAccessDto> PutAsync(Guid applicationId, Guid userId, ManagementApplicationAccessRequest request, CancellationToken ct = default);
    Task RevokeAsync(Guid applicationId, Guid userId, string expectedVersion, CancellationToken ct = default);
    /// <summary>Live organization, user, application and assignment checks; no role bypass or authorization cache.</summary>
    Task RequireAsync(Guid applicationId, string permission, CancellationToken ct = default);
}
