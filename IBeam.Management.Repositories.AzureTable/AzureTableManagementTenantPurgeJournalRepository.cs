using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.Operations;
using IBeam.Management.TenantData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementTenantPurgeJournalRepository(TableClient dedicatedTable) : IManagementTenantPurgeJournalRepository
{
    public int ProtocolVersion => 1;
    public bool Durable => true;
    private static string Partition(ManagementTenantPurgeExecution e) => "PURGE|" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        e.ApplicationKey, e.Environment, e.Authority.OrganizationId, e.Authority.ApplicationId, e.Authority.RealmId, e.Authority.TenantId })));
    private static string Row(string kind, Guid id) => (kind == "plan" ? "P|" : "J|") + id.ToString("N");
    private static bool SameScope(ManagementTenantPurgeExecution a, ManagementTenantPurgeExecution b) => a.ApplicationKey == b.ApplicationKey && a.Environment == b.Environment &&
        a.Authority.OrganizationId == b.Authority.OrganizationId && a.Authority.ApplicationId == b.Authority.ApplicationId && a.Authority.RealmId == b.Authority.RealmId &&
        a.Authority.TenantId == b.Authority.TenantId && a.Authority.ActorUserId == b.Authority.ActorUserId && a.Authority.ResourceId == b.Authority.ResourceId;
    private static void Valid(ManagementTenantPurgeJournalEntity entity)
    {
        var e = entity.Execution; var a = e.Authority;
        if (entity.Kind is not ("plan" or "job") || e.JobId == Guid.Empty || a.OperationId != e.JobId || a.TenantId == Guid.Empty || a.ResourceId != a.TenantId ||
            a.OrganizationId == Guid.Empty || a.ApplicationId == Guid.Empty || a.RealmId == Guid.Empty || a.ActorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(e.ApplicationKey) || string.IsNullOrWhiteSpace(e.Environment) || entity.Plan.PlanId == Guid.Empty ||
            entity.Kind == "plan" && entity.Plan.PlanId != e.JobId) throw new ManagementOperationInvalidException();
    }
    private static TableEntity Encode(ManagementTenantPurgeJournalEntity entity)
    {
        Valid(entity);
        return new(Partition(entity.Execution), Row(entity.Kind, entity.Execution.JobId)) {
            ["Metadata"] = JsonSerializer.Serialize(entity with { Version = "" }), ["Kind"] = entity.Kind,
            ["ApplicationKey"] = entity.Execution.ApplicationKey, ["Environment"] = entity.Execution.Environment,
            ["RealmId"] = entity.Execution.Authority.RealmId, ["OrganizationId"] = entity.Execution.Authority.OrganizationId, ["ApplicationId"] = entity.Execution.Authority.ApplicationId };
    }
    private static ManagementTenantPurgeJournalEntity Decode(TableEntity entity)
    {
        var value = JsonSerializer.Deserialize<ManagementTenantPurgeJournalEntity>(entity.GetString("Metadata")) ?? throw new ManagementOperationUnavailableException();
        Valid(value);
        if (Partition(value.Execution) != entity.PartitionKey || Row(value.Kind, value.Execution.JobId) != entity.RowKey) throw new ManagementOperationUnavailableException();
        return value with { Version = entity.ETag.ToString() };
    }
    private async Task<ManagementTenantPurgeJournalEntity?> Get(ManagementTenantPurgeExecution scope, string kind, Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ManagementOperationInvalidException();
        var found = await dedicatedTable.GetEntityIfExistsAsync<TableEntity>(Partition(scope), Row(kind, id), cancellationToken: ct);
        if (!found.HasValue) return null;
        var row = Decode(found.Value);
        if (row.Kind != kind || row.Execution.JobId != id || !SameScope(row.Execution, scope) || kind == "job" && (row.Execution.JobId != scope.JobId || row.Execution.Authority.OperationId != scope.Authority.OperationId))
            throw new ManagementAccessDeniedException();
        return row;
    }
    public Task<ManagementTenantPurgeJournalEntity?> GetPlanAsync(ManagementTenantPurgeExecution scope, Guid planId, CancellationToken ct = default) => Get(scope, "plan", planId, ct);
    public Task<ManagementTenantPurgeJournalEntity?> GetJobAsync(ManagementTenantPurgeExecution scope, CancellationToken ct = default) => Get(scope, "job", scope.JobId, ct);
    public async Task<bool> TryCreatePlanAsync(ManagementTenantPurgeJournalEntity entity, CancellationToken ct = default)
    {
        if (entity.Kind != "plan" || entity.ConsumedJobId is not null) throw new ManagementOperationInvalidException();
        try { await dedicatedTable.AddEntityAsync(Encode(entity), ct); return true; }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
    }
    public async Task<bool> TryConfirmAsync(ManagementTenantPurgeJournalEntity plan, string expectedPlanVersion, ManagementTenantPurgeJournalEntity job, CancellationToken ct = default)
    {
        Etag(expectedPlanVersion); Valid(plan); Valid(job);
        if (plan.Kind != "plan" || job.Kind != "job" || plan.ConsumedJobId != job.Execution.JobId || job.ConsumedJobId is not null ||
            job.Execution.JobId == plan.Execution.JobId || job.Plan.PlanId != plan.Execution.JobId || !SameScope(plan.Execution, job.Execution) || Partition(plan.Execution) != Partition(job.Execution))
            throw new ManagementOperationInvalidException();
        try {
            await dedicatedTable.SubmitTransactionAsync(new[] {
                new TableTransactionAction(TableTransactionActionType.UpdateReplace, Encode(plan), new ETag(expectedPlanVersion)),
                new TableTransactionAction(TableTransactionActionType.Add, Encode(job)) }, ct);
            return true;
        } catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412) { return false; }
    }
    public async Task<ManagementTenantPurgeJournalEntity> ReplaceJobAsync(ManagementTenantPurgeJournalEntity entity, string expectedVersion, CancellationToken ct = default)
    {
        Etag(expectedVersion); if (entity.Kind != "job") throw new ManagementOperationInvalidException();
        try {
            var response = await dedicatedTable.UpdateEntityAsync(Encode(entity), new ETag(expectedVersion), TableUpdateMode.Replace, ct);
            return entity with { Version = response.Headers.ETag?.ToString() ?? throw new ManagementOperationUnavailableException() };
        } catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new ManagementOperationConflictException(); }
    }
    public async Task<ManagementTenantPurgeJobPage> ListPageAsync(ManagementTenantPurgeDeployment scope, int pageSize, string? continuationToken, CancellationToken ct = default)
    {
        if (pageSize is < 1 or > 25 || continuationToken?.Length > 4096 || string.IsNullOrWhiteSpace(scope.ApplicationKey) || string.IsNullOrWhiteSpace(scope.Environment) ||
            scope.RealmId == Guid.Empty || scope.OrganizationId == Guid.Empty || scope.ApplicationId == Guid.Empty) throw new ManagementOperationInvalidException();
        var filter = TableClient.CreateQueryFilter($"Kind eq {"job"} and ApplicationKey eq {scope.ApplicationKey} and Environment eq {scope.Environment} and RealmId eq {scope.RealmId} and OrganizationId eq {scope.OrganizationId} and ApplicationId eq {scope.ApplicationId}");
        await foreach (var page in dedicatedTable.QueryAsync<TableEntity>(filter, pageSize, ["PartitionKey", "RowKey", "Metadata"], ct).AsPages(continuationToken, pageSize).WithCancellation(ct)) {
            var handles = new List<ManagementTenantPurgeExecution>();
            foreach (var entity in page.Values) {
                var row = Decode(entity); var e = row.Execution;
                if (row.Kind != "job" || e.ApplicationKey != scope.ApplicationKey || e.Environment != scope.Environment || e.Authority.RealmId != scope.RealmId ||
                    e.Authority.OrganizationId != scope.OrganizationId || e.Authority.ApplicationId != scope.ApplicationId) throw new ManagementOperationUnavailableException();
                handles.Add(e);
            }
            return new(handles.AsReadOnly(), page.ContinuationToken);
        }
        return new([], null);
    }
    private static void Etag(string version) { if (string.IsNullOrWhiteSpace(version) || version.Trim() == "*") throw new ManagementOperationConflictException(); }
}
public static class ManagementTenantPurgeStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableTenantPurgeJournal(this IServiceCollection services, TableClient dedicatedTable)
    {
        ArgumentNullException.ThrowIfNull(dedicatedTable);
        services.TryAddScoped<IManagementTenantPurgeJournalRepository>(_ => new AzureTableManagementTenantPurgeJournalRepository(dedicatedTable));
        return services;
    }
}
