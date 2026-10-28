using System.Text.Json.Serialization;

namespace IBeam.Management.TenantData;

/// <summary>One journal entity type, with immutable plan rows and leased job rows. Metadata only; no customer records or storage selectors.</summary>
public sealed record ManagementTenantPurgeJournalEntity
{
    public required string Kind { get; init; }
    public required ManagementTenantPurgeExecution Execution { get; init; }
    public required ManagementTenantPurgeDefinition Definition { get; init; }
    public required ManagementTenantPurgePlan Plan { get; init; }
    public required string IntentHash { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required DateTimeOffset UpdatedUtc { get; init; }
    public required string[] FenceKeys { get; init; }
    public Guid? ConsumedJobId { get; init; }
    public string State { get; init; } = "requested";
    public string Phase { get; init; } = "fencing";
    public ManagementTenantPurgeCategory[] Coverage { get; init; } = [];
    public Guid? LeaseId { get; init; }
    public DateTimeOffset? LeaseExpiresUtc { get; init; }
    public bool NeedsReconciliation { get; init; }
    public bool WorkStarted { get; init; }
    public string? FailureCode { get; init; }
    [JsonIgnore] public string Version { get; init; } = "";
    public override string ToString() => "Tenant purge journal (operational metadata only)";
}
/// <summary>Entity-only persistence. Plan consumption plus job creation is one conditional transaction in the same tenant partition.
/// Point operations verify the original deployment/org/app/realm/tenant/person scope; every replacement uses an exact ETag.</summary>
public interface IManagementTenantPurgeJournalRepository
{
    int ProtocolVersion { get; }
    bool Durable { get; }
    Task<ManagementTenantPurgeJournalEntity?> GetPlanAsync(ManagementTenantPurgeExecution scope, Guid planId, CancellationToken ct = default);
    Task<ManagementTenantPurgeJournalEntity?> GetJobAsync(ManagementTenantPurgeExecution scope, CancellationToken ct = default);
    Task<bool> TryCreatePlanAsync(ManagementTenantPurgeJournalEntity entity, CancellationToken ct = default);
    Task<bool> TryConfirmAsync(ManagementTenantPurgeJournalEntity consumedPlan, string expectedPlanVersion, ManagementTenantPurgeJournalEntity job, CancellationToken ct = default);
    Task<ManagementTenantPurgeJournalEntity> ReplaceJobAsync(ManagementTenantPurgeJournalEntity entity, string expectedVersion, CancellationToken ct = default);
    Task<ManagementTenantPurgeJobPage> ListPageAsync(ManagementTenantPurgeDeployment deployment, int pageSize, string? continuationToken, CancellationToken ct = default);
}
public sealed record ManagementTenantPurgeDeployment(string ApplicationKey, string Environment, Guid OrganizationId, Guid ApplicationId, Guid RealmId);
public sealed record ManagementTenantPurgeJobPage(IReadOnlyList<ManagementTenantPurgeExecution> Items, string? ContinuationToken);

/// <summary>Required native integration, not a support endpoint. A fence is durable, job-owned and denies all customer requests,
/// refreshes and writes before cleanup. It is conditional on the exact planned tenant revision and cannot be undone by status activation.
/// Reconcile by full scoped job identity; never infer success from a deactivated flag. Multiple native/app fences can participate.</summary>
public interface IManagementTenantPurgeAccessFence
{
    int ProtocolVersion { get; }
    string Key { get; }
    Task FenceOrReconcileAsync(ManagementTenantPurgeExecution execution, string expectedTenantVersion, CancellationToken ct = default);
    Task RequireFencedAsync(ManagementTenantPurgeExecution execution, CancellationToken ct = default);
}
/// <summary>Trusted server-side worker. Management HTTP only submits and polls; it never invokes reconciliation or cancellation.</summary>
public interface IManagementTenantPurgeWorker
{
    Task RunAsync(ManagementTenantPurgeExecution execution, bool reconcileUncertain = false, CancellationToken ct = default);
    Task CancelBeforeFenceAsync(ManagementTenantPurgeExecution execution, CancellationToken ct = default);
}
/// <summary>Positive evidence that the same native job is still running. Only this signal permits unattended same-job resumption.</summary>
public sealed class ManagementTenantPurgePendingException() : Exception("The native purge operation is still running.");
public sealed class ManagementTenantPurgePermanentFailureException : Exception
{
    public string Code { get; }
    public ManagementTenantPurgePermanentFailureException(string code) : base("The native purge could not complete.")
        => Code = code is "fence-failed" or "handler-failed" or "verification-failed" ? code : "handler-failed";
}
