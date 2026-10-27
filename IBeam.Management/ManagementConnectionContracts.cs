using System.Text.Json.Serialization;
using IBeam.Management.Entities;
namespace IBeam.Management;

public readonly record struct ManagementSecretScope(Guid OrganizationId, Guid ApplicationId);
public sealed class ManagementSecretValue
{
    [JsonIgnore] public string Value { get; }
    public ManagementSecretValue(string value) => Value = value;
    public override string ToString() => "Management secret (redacted)";
}
public interface IManagementSecretStore
{
    bool SupportsWrites { get; }
    Task<string> CreateAsync(ManagementSecretScope scope, ManagementSecretValue secret, CancellationToken ct = default);
    Task<ManagementSecretValue> ReadAsync(ManagementSecretScope scope, string reference, CancellationToken ct = default);
}
public interface IManagementApplicationCredentialRepository
{
    Task<ManagementApplicationCredentialEntity?> GetAsync(Guid organizationId, Guid applicationId, Guid credentialId, CancellationToken ct = default);
    Task CreateAsync(ManagementApplicationCredentialEntity credential, CancellationToken ct = default);
}
public interface IManagementTargetConnectionProbe
{
    /// <summary>Only approved origins; verifies TLS, pins validated public DNS answers, never follows redirects or returns raw errors.</summary>
    Task<ManagementDiagnosticsInfo> ProbeAsync(string origin, ManagementSecretValue key, CancellationToken ct = default);
}
public sealed class UpdateManagementConnection
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public string ApiKey { get; set; } = "";
    public string ExpectedVersion { get; set; } = "";
    public override string ToString() => "Management connection request (API key redacted)";
}
public sealed record ManagementConnectionResult(Guid ApplicationId, string Version, DateTimeOffset VerifiedUtc, IReadOnlyList<string> Capabilities)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CustomerIdentity.ManagementCustomerIdentityDiscovery? CustomerIdentity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Operations.ManagementRegisteredOperationDiscovery? Operations { get; init; }
}
public interface IManagementApplicationConnectionService
{
    Task<ManagementConnectionResult> PutAsync(Guid applicationId, UpdateManagementConnection request, CancellationToken ct = default);
    Task<ManagementConnectionResult> TestAsync(Guid applicationId, CancellationToken ct = default);
}
public sealed class ManagementConnectionInvalidException() : Exception("The connection destination, credential or target identity is invalid.");
public sealed class ManagementConnectionUnavailableException() : Exception("The management connection is temporarily unavailable.");
public sealed class ManagementConnectionTimeoutException() : Exception("The management connection timed out.");
public sealed class ManagementSecretStoreUnavailableException() : Exception("Management secret storage is unavailable.");
