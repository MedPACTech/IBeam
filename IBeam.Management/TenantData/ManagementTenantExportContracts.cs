using System.Security.Claims;
using System.Text.Json.Serialization;
using IBeam.Management.Operations;

namespace IBeam.Management.TenantData;

public static class ManagementTenantExportStates
{
    public const string Requested = "requested";
    public const string Running = "running";
    public const string PartiallyCompleted = "partially-completed";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Requested, Running, PartiallyCompleted, Succeeded, Failed, Cancelled, Expired });
}

/// <summary>Compiled framework routes. These constants install no endpoint or authority.</summary>
public static class ManagementTenantExportRoutes
{
    public const string Submit = "/api/management-tenant-data/tenants/{tenantId:guid}/exports";
    public const string Status = "/api/management-tenant-data/tenants/{tenantId:guid}/exports/{jobId:guid}";
    public const string PortalSubmit = "api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}/exports";
    public const string PortalStatus = PortalSubmit + "/{jobId:guid}";
    public static string StatusPath(Guid tenant, Guid job) => $"/api/management-tenant-data/tenants/{tenant:D}/exports/{job:D}";
    public static bool IsStatusPath(string path)
    {
        if (path is null) return false;
        var parts = path.Split('/');
        return parts.Length == 7 && parts[0] == "" && parts[1] == "api" && parts[2] == "management-tenant-data" && parts[3] == "tenants" && parts[5] == "exports" &&
            Guid.TryParseExact(parts[4], "D", out var tenant) && tenant != Guid.Empty && Guid.TryParseExact(parts[6], "D", out var job) && job != Guid.Empty;
    }
}

/// <summary>Policy keys select target-owned inventory, consistency and recipient rules; no destination or recipient input.</summary>
public sealed record ManagementTenantExportRequest(string PolicyKey, string ExpectedVersion);
public sealed record ManagementTenantExportCoverage(string Key, string Kind, string Disposition);
/// <summary>Safe operational status only. There is deliberately no URL, artifact handle, recipient, filename or payload.</summary>
public sealed record ManagementTenantExportJobStatus(int ContractVersion, Guid TenantId, Guid JobId, string State,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, DateTimeOffset ExpiresUtc, string Consistency,
    ManagementTenantExportCoverage[] Coverage, string? FailureCode);
/// <summary>Acceptance/lookup outcome is separate from business job state. Unknown is never reported as succeeded.</summary>
public sealed record ManagementTenantExportResult(Guid TenantId, Guid JobId, string Outcome, ManagementTenantExportJobStatus? Job);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantExportAction(string PolicyKey);
public interface IManagementTenantExportPortalService
{
    Task<ManagementTenantExportResult> SubmitAsync(Guid applicationId, Guid tenantId, ManagementTenantExportRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<ManagementTenantExportResult> FindAsync(Guid applicationId, Guid tenantId, Guid jobId, CancellationToken ct = default);
}
/// <summary>Same fixed contract on portal and target; does not install a handler or grant authority.</summary>
public static class ManagementTenantExportOperation
{
    public static ManagementOperationDescriptor Descriptor { get; } = new(ManagementTenantDataOperations.Export, "POST", ManagementTenantExportRoutes.Submit,
        new(typeof(ManagementTenantExportRequest), "management.tenant-export.submit.request"), new(typeof(ManagementTenantExportResult), "management.tenant-export.job.result"),
        typeof(IManagementOperationHandler<ManagementTenantExportRequest, ManagementTenantExportResult>), Array.AsReadOnly(new[] { ManagementTenantDataOperations.Export }), 1, ManagementTenantDataOperations.Export);
}

public sealed record ManagementTenantExportDataset(string Key, string Kind);
/// <summary>Trusted static inventory declared by the consumer, never discovered from customer record names.</summary>
public sealed record ManagementTenantExportDefinition(int ContractVersion, string HandlerKey, int InventoryVersion,
    string Consistency, IReadOnlyList<ManagementTenantExportDataset> Datasets);
public sealed record ManagementTenantExportExecution(ManagementOperationContext Authority, string ApplicationKey, string Environment,
    Guid JobId, string PolicyKey, int InventoryVersion)
{
    [JsonIgnore] public IManagementTenantExportLeaseFence? LeaseFence { get; init; }
    [JsonIgnore] public DateTimeOffset? ArtifactExpiresUtc { get; init; }
}
/// <summary>Consumer providers must check the current lease before publishing output or committing delivery, and reconcile by stable job ID.</summary>
public interface IManagementTenantExportLeaseFence
{
    DateTimeOffset ExpiresUtc { get; }
    Task RequireAsync(CancellationToken ct = default);
}

/// <summary>Private server-side reference. It must not be returned through Management HTTP or audit.</summary>
public sealed class ManagementTenantExportArtifact
{
    [JsonIgnore] public string Reference { get; }
    public DateTimeOffset ExpiresUtc { get; }
    public ManagementTenantExportArtifact(string reference, DateTimeOffset expiresUtc)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 4096) throw new ManagementOperationInvalidException();
        Reference = reference; ExpiresUtc = expiresUtc;
    }
    public override string ToString() => "Private tenant export artifact (reference redacted)";
}
public sealed class ManagementTenantExportOutput
{
    [JsonIgnore] public ManagementTenantExportArtifact Artifact { get; }
    [JsonIgnore] public IReadOnlyList<ManagementTenantExportCoverage> Coverage { get; }
    public ManagementTenantExportOutput(ManagementTenantExportArtifact artifact, IReadOnlyList<ManagementTenantExportCoverage> coverage)
    { Artifact = artifact; Coverage = coverage; }
    public override string ToString() => "Private tenant export output (artifact redacted)";
}

/// <summary>App-owned snapshot/export work. Reconcile by stable scoped job ID after crashes; do not enumerate current seats as data ownership.</summary>
public interface IManagementTenantExportHandler
{
    int ProtocolVersion { get; }
    ManagementTenantExportDefinition Describe();
    Task<ManagementTenantExportOutput> ExecuteOrResumeAsync(ManagementTenantExportExecution execution, CancellationToken ct = default);
    Task CancelAsync(ManagementTenantExportExecution execution, CancellationToken ct = default);
}
/// <summary>App-owned delivery/retention. Resolve an approved customer recipient from policy; never deliver to a support principal by inference.</summary>
public interface IManagementTenantExportDelivery
{
    int ProtocolVersion { get; }
    Task RequireArtifactAsync(ManagementTenantExportExecution execution, ManagementTenantExportArtifact artifact, CancellationToken ct = default);
    Task DeliverOrReconcileAsync(ManagementTenantExportExecution execution, ManagementTenantExportArtifact artifact, CancellationToken ct = default);
    Task RevokeAndDeleteAsync(ManagementTenantExportExecution execution, ManagementTenantExportArtifact artifact, CancellationToken ct = default);
}

/// <summary>Trusted target scheduler/administration port, never mapped to a support HTTP route.</summary>
public interface IManagementTenantExportWorker
{
    Task RunAsync(ManagementTenantExportExecution execution, bool reconcileUncertain = false, CancellationToken ct = default);
    Task CancelAsync(ManagementTenantExportExecution execution, CancellationToken ct = default);
    Task CleanupAsync(ManagementTenantExportExecution execution, CancellationToken ct = default);
}
/// <summary>Server-only app customer retrieval gate. The support API never exposes this port or an artifact download.</summary>
public interface IManagementTenantExportCustomerArtifacts
{
    Task<ManagementTenantExportArtifact> RequireAsync(ManagementTenantExportExecution execution, ClaimsPrincipal authenticatedCustomer, CancellationToken ct = default);
}
public sealed class ManagementTenantExportPermanentFailureException : Exception
{
    public string Code { get; }
    public ManagementTenantExportPermanentFailureException(string code) : base("The consumer export could not complete.")
        => Code = code is "handler-failed" or "delivery-denied" ? code : "handler-failed";
}
/// <summary>Provider positively knows the same durable consumer job/delivery is still running. Reconciliation may resume by that job ID.</summary>
public sealed class ManagementTenantExportPendingException() : Exception("The consumer export is still running.");
/// <summary>Required app-owned authorization for the separate customer retrieval surface. Support grants are not customer authority.</summary>
public interface IManagementTenantExportCustomerAuthorization
{
    int ProtocolVersion { get; }
    Task RequireAsync(Guid realmId, Guid tenantId, Guid jobId, ClaimsPrincipal authenticatedCustomer, CancellationToken ct = default);
}
/// <summary>Submission policy verifies native tenant version, export eligibility and a target-owned delivery policy.</summary>
public interface IManagementTenantExportPolicy
{
    int ProtocolVersion { get; }
    Task RequireAsync(ManagementTenantExportExecution execution, string expectedTenantVersion, CancellationToken ct = default);
    Task RequireStatusAsync(ManagementTenantExportExecution storedExecution, CancellationToken ct = default);
}
/// <summary>Durable orchestration port. Submission and immutable same-intent metadata must commit atomically.
/// Find is read-only, matches the original org/app/environment/realm/tenant/person/job scope, and rechecks current status policy.</summary>
public interface IManagementTenantExportJobService
{
    int ProtocolVersion { get; }
    bool Durable { get; }
    Task<ManagementTenantExportResult> SubmitAsync(ManagementTenantExportExecution execution, ManagementTenantExportRequest request, CancellationToken ct = default);
    Task<ManagementTenantExportResult> FindAsync(ManagementTenantExportExecution execution, CancellationToken ct = default);
}
