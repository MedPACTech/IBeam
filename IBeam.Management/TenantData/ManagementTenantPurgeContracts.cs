using System.Text.Json.Serialization;
using IBeam.Management.Operations;

namespace IBeam.Management.TenantData;

/// <summary>Compiled intent routes; declaring a route installs neither endpoints nor authority.</summary>
public static class ManagementTenantPurgeRoutes
{
    public const string Command = "/api/management-operations/tenants/{tenantId:guid}/purges";
    public const string PortalPlan = "api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}/purge-plans";
    public const string PortalConfirm = "api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}/purges";
    public const string PortalStatus = PortalConfirm + "/{jobId:guid}";
}

/// <summary>One independent delete capability covers plan and confirm; Stage never selects an arbitrary handler or route.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantPurgeRequest(string Stage, string PolicyKey, string ExpectedVersion, Guid? PlanId, string? ConfirmationBinding);
public sealed record ManagementTenantPurgeCategory(string Key, string Kind, string Disposition);
public sealed record ManagementTenantPurgePlan(int ContractVersion, Guid TenantId, Guid PlanId, string PolicyKey,
    string ExpectedVersion, string InventoryFingerprint, string ConfirmationBinding, DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc, ManagementTenantPurgeCategory[] Categories);
public sealed record ManagementTenantPurgeJobStatus(int ContractVersion, Guid TenantId, Guid JobId, Guid PlanId, string State,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, ManagementTenantPurgeCategory[] Coverage, string? FailureCode);
/// <summary>Operational metadata only: no business values, storage selectors, account credentials or deletion credentials.</summary>
public sealed record ManagementTenantPurgeResult(Guid TenantId, Guid OperationId, string Outcome,
    ManagementTenantPurgePlan? Plan, ManagementTenantPurgeJobStatus? Job);
public static class ManagementTenantPurgeOperation
{
    public static ManagementOperationDescriptor Descriptor { get; } = new(ManagementTenantDataOperations.Delete, "POST", ManagementTenantPurgeRoutes.Command,
        new(typeof(ManagementTenantPurgeRequest), "management.tenant-purge.command.request"), new(typeof(ManagementTenantPurgeResult), "management.tenant-purge.command.result"),
        typeof(IManagementOperationHandler<ManagementTenantPurgeRequest, ManagementTenantPurgeResult>),
        Array.AsReadOnly(new[] { ManagementTenantDataOperations.Delete }), 1, ManagementTenantDataOperations.Delete);
}

/// <summary>Trusted static inventory. Global accounts must be explicitly excluded; this contract grants no global-user deletion authority.</summary>
public sealed record ManagementTenantPurgeDefinition(int ContractVersion, string HandlerKey, int InventoryVersion,
    IReadOnlyList<ManagementTenantPurgeCategory> Categories);
public sealed record ManagementTenantPurgeExecution(ManagementOperationContext Authority, string ApplicationKey, string Environment,
    Guid JobId, string PolicyKey, string InventoryFingerprint)
{
    [JsonIgnore] public IManagementTenantPurgeLeaseFence? LeaseFence { get; init; }
}
/// <summary>A consumer checks the current lease immediately before committing every destructive write.</summary>
public interface IManagementTenantPurgeLeaseFence
{
    DateTimeOffset ExpiresUtc { get; }
    Task RequireAsync(CancellationToken ct = default);
    /// <summary>Optional journal-only proof for a native fence validating itself; avoids recursive checks of that same fence.</summary>
    int JournalProtocolVersion => 0;
    Task RequireJournalAsync(CancellationToken ct = default) => throw new ManagementOperationUnavailableException();
}
/// <summary>App-owned exact-tenant cleanup. Preview is read-only. Destruction must reconcile by the stable fully scoped job and require its lease/write fence.</summary>
public interface IManagementTenantPurgeHandler
{
    int ProtocolVersion { get; }
    ManagementTenantPurgeDefinition Describe();
    Task PreviewAsync(ManagementTenantPurgeExecution execution, CancellationToken ct = default);
    Task PurgeOrReconcileAsync(ManagementTenantPurgeExecution execution, CancellationToken ct = default);
    Task<IReadOnlyList<ManagementTenantPurgeCategory>> VerifyAsync(ManagementTenantPurgeExecution execution, CancellationToken ct = default);
}
/// <summary>App-owned native eligibility and exact tenant revision, checked afresh for planning/confirmation and status.</summary>
public interface IManagementTenantPurgePolicy
{
    int ProtocolVersion { get; }
    Task RequireAsync(ManagementTenantPurgeExecution execution, string expectedVersion, CancellationToken ct = default);
    Task RequireStatusAsync(ManagementTenantPurgeExecution execution, CancellationToken ct = default);
}
/// <summary>Durable atomic plan/job boundary. All calls bind the original app/environment/org/realm/tenant/person scope.
/// CreatePlan returns the same immutable prior intent or conflicts. Confirm atomically checks the unexpired exact plan,
/// consumes it once and creates one stable job; replay of that same job reads its receipt, and another job conflicts.
/// It must recheck native revision/write fencing at the commit boundary. No destructive hook runs inline here.</summary>
public interface IManagementTenantPurgeJobService
{
    int ProtocolVersion { get; }
    bool Durable { get; }
    Task<ManagementTenantPurgePlan> CreatePlanAsync(ManagementTenantPurgeExecution execution, ManagementTenantPurgePlan proposal, CancellationToken ct = default);
    Task<ManagementTenantPurgePlan?> ReadPlanAsync(ManagementTenantPurgeExecution execution, Guid planId, CancellationToken ct = default);
    Task<ManagementTenantPurgeResult> ConfirmAsync(ManagementTenantPurgeExecution execution, ManagementTenantPurgePlan plan, CancellationToken ct = default);
    Task<ManagementTenantPurgeResult> FindAsync(ManagementTenantPurgeExecution execution, CancellationToken ct = default);
}
