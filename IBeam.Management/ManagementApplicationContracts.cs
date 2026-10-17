using IBeam.Management.Entities;
namespace IBeam.Management;

public static class ManagementApplicationOperations
{
    public const string Admin = "management.applications.admin";
}
public interface IManagementApplicationRepository
{
    Task<ManagementApplicationEntity?> GetAsync(Guid organizationId, Guid applicationId, CancellationToken ct = default);
    Task<ManagementApplicationPage> ListAsync(Guid organizationId, Guid? after, int pageSize, CancellationToken ct = default);
    Task<ManagementApplicationEntity> CreateAsync(ManagementApplicationEntity application, CancellationToken ct = default);
    Task<ManagementApplicationEntity> ReplaceAsync(ManagementApplicationEntity application, string expectedVersion, CancellationToken ct = default);
}
public sealed record ManagementApplicationPage(IReadOnlyList<ManagementApplicationEntity> Items, Guid? Next);
public sealed record ManagementApplicationDto(Guid ApplicationId, string ApplicationKey, string Name,
    string Environment, string BaseUrl, bool IsEnabled, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
    string Version, bool HasConnection);
public sealed class CreateManagementApplication
{
    public string ApplicationKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Environment { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}
public sealed class UpdateManagementApplication
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string ExpectedVersion { get; set; } = "";
}
public sealed record ManagementApplicationList(IReadOnlyList<ManagementApplicationDto> Items, int PageSize, string? ContinuationToken);
public interface IManagementApplicationService
{
    Task<ManagementApplicationList> ListAsync(int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
    Task<ManagementApplicationDto> GetAsync(Guid applicationId, CancellationToken ct = default);
    Task<ManagementApplicationDto> CreateAsync(CreateManagementApplication request, CancellationToken ct = default);
    Task<ManagementApplicationDto> UpdateAsync(Guid applicationId, UpdateManagementApplication request, CancellationToken ct = default);
}
public sealed class ManagementApplicationInvalidException() : Exception("The management application request is invalid.");
public sealed class ManagementApplicationConflictException() : Exception("The application already exists or changed concurrently.");
public sealed class ManagementApplicationNotFoundException() : Exception("Management application was not found.");
public sealed class ManagementApplicationUnavailableException() : Exception("Management applications are temporarily unavailable.");
