using System.Text.Json.Serialization;

namespace IBeam.Management.TenantData;

/// <summary>Dedicated scoped journal. Private artifact recovery is authenticated ciphertext in a separate column, never safe metadata.</summary>
public sealed record ManagementTenantExportJobEntity
{
    public required ManagementTenantExportExecution Execution { get; init; }
    public required ManagementTenantExportDefinition Definition { get; init; }
    public required string IntentHash { get; init; }
    public required string ExpectedTenantVersion { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required DateTimeOffset UpdatedUtc { get; init; }
    public required DateTimeOffset ExpiresUtc { get; init; }
    public string State { get; init; } = ManagementTenantExportStates.Requested;
    public string Phase { get; init; } = "export";
    public ManagementTenantExportCoverage[] Coverage { get; init; } = [];
    public Guid? LeaseId { get; init; }
    public DateTimeOffset? LeaseExpiresUtc { get; init; }
    public bool NeedsReconciliation { get; init; }
    public bool CancelRequested { get; init; }
    public bool Cleaned { get; init; }
    public string? FailureCode { get; init; }
    [JsonIgnore] public string ProtectedArtifact { get; init; } = "";
    [JsonIgnore] public string Version { get; init; } = "";
    public override string ToString() => "Tenant export journal (private recovery redacted)";
}
/// <summary>Entity-only persistence. Exact ETags fence all journal mutations, including leases/cancellation. Runtime reads never create schema.</summary>
public interface IManagementTenantExportJobRepository
{
    int ProtocolVersion { get; }
    bool Durable { get; }
    Task<ManagementTenantExportJobEntity?> GetAsync(ManagementTenantExportExecution scope, CancellationToken ct = default);
    Task<bool> TryCreateAsync(ManagementTenantExportJobEntity entity, CancellationToken ct = default);
    Task<ManagementTenantExportJobEntity> ReplaceAsync(ManagementTenantExportJobEntity entity, string expectedVersion, CancellationToken ct = default);
    Task<ManagementTenantExportJobPage> ListPageAsync(ManagementTenantExportDeployment deployment, int pageSize, string? continuationToken, CancellationToken ct = default);
}
public sealed record ManagementTenantExportDeployment(string ApplicationKey, string Environment, Guid OrganizationId, Guid ApplicationId, Guid RealmId);
public sealed record ManagementTenantExportJobPage(IReadOnlyList<ManagementTenantExportExecution> Items, string? ContinuationToken);
