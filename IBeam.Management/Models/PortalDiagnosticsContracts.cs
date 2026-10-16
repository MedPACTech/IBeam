using System.Text.Json.Serialization;
namespace IBeam.Management.Models;

public sealed class ManagementDiagnosticConnection
{
    public Guid OrganizationId { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid UserId { get; init; }
    public Guid CredentialId { get; init; }
    public string ApplicationKey { get; init; } = "";
    public string Environment { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string Version { get; init; } = "";
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    [JsonIgnore] public ManagementSecretValue Key { get; init; } = new("");
    public override string ToString() => "Management diagnostic connection (key redacted)";
}
public interface IManagementDiagnosticConnectionResolver
{
    Task<ManagementDiagnosticConnection> ResolveAsync(Guid applicationId, string permission, CancellationToken ct = default);
}
public interface IApplicationDiagnosticsClient
{
    Task<ManagementDiagnosticsInfo> InfoAsync(ManagementDiagnosticConnection connection, CancellationToken ct = default);
    Task<SystemErrorPage> ErrorsAsync(ManagementDiagnosticConnection connection, DiagnosticQuery query, CancellationToken ct = default);
    Task<SystemErrorDto> ErrorAsync(ManagementDiagnosticConnection connection, string id, CancellationToken ct = default);
    Task<SystemLogPage> LogsAsync(ManagementDiagnosticConnection connection, SystemLogQuery query, CancellationToken ct = default);
    Task<SystemLogDto> LogAsync(ManagementDiagnosticConnection connection, string id, CancellationToken ct = default);
}
public interface IManagementDiagnosticsService
{
    Task<SystemErrorPage> ErrorsAsync(Guid applicationId, DiagnosticQuery query, CancellationToken ct = default);
    Task<SystemErrorDto> ErrorAsync(Guid applicationId, string id, CancellationToken ct = default);
    Task<SystemLogPage> LogsAsync(Guid applicationId, SystemLogQuery query, CancellationToken ct = default);
    Task<SystemLogDto> LogAsync(Guid applicationId, string id, CancellationToken ct = default);
}
public sealed class ManagementPortalResponseException() : Exception("The application returned an invalid diagnostic response.");
public sealed class ManagementPortalRateLimitException() : Exception("The application diagnostic request was rate limited.");
