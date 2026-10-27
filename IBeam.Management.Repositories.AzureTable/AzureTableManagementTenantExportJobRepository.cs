using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.Operations;
using IBeam.Management.TenantData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementTenantExportJobRepository(TableClient dedicatedTable) : IManagementTenantExportJobRepository
{
    public int ProtocolVersion => 1;
    public bool Durable => true;
    private static string Partition(ManagementTenantExportExecution e) => "EXPORT|" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        e.ApplicationKey, e.Environment, e.Authority.OrganizationId, e.Authority.ApplicationId, e.Authority.RealmId })));
    private static string Row(ManagementTenantExportExecution e) => e.Authority.TenantId.ToString("N") + "|" + e.JobId.ToString("N");
    private static bool Same(ManagementTenantExportExecution a, ManagementTenantExportExecution b) => a.ApplicationKey == b.ApplicationKey && a.Environment == b.Environment && a.JobId == b.JobId &&
        a.Authority.OrganizationId == b.Authority.OrganizationId && a.Authority.ApplicationId == b.Authority.ApplicationId && a.Authority.RealmId == b.Authority.RealmId &&
        a.Authority.TenantId == b.Authority.TenantId && a.Authority.ActorUserId == b.Authority.ActorUserId && a.Authority.OperationId == b.Authority.OperationId && a.Authority.ResourceId == b.Authority.ResourceId;
    private static TableEntity Encode(ManagementTenantExportJobEntity row) => new(Partition(row.Execution), Row(row.Execution)) {
        ["Metadata"] = JsonSerializer.Serialize(row with { Version = "" }), ["ProtectedArtifact"] = row.ProtectedArtifact,
        ["ApplicationKey"] = row.Execution.ApplicationKey, ["Environment"] = row.Execution.Environment, ["RealmId"] = row.Execution.Authority.RealmId };
    public async Task<ManagementTenantExportJobEntity?> GetAsync(ManagementTenantExportExecution scope, CancellationToken ct = default)
    {
        var result = await dedicatedTable.GetEntityIfExistsAsync<TableEntity>(Partition(scope), Row(scope), cancellationToken: ct);
        if (!result.HasValue) return null;
        var row = JsonSerializer.Deserialize<ManagementTenantExportJobEntity>(result.Value.GetString("Metadata")) ?? throw new ManagementOperationUnavailableException();
        if (!Same(row.Execution, scope)) throw new ManagementAccessDeniedException();
        return row with { ProtectedArtifact = result.Value.GetString("ProtectedArtifact") ?? "", Version = result.Value.ETag.ToString() };
    }
    public async Task<bool> TryCreateAsync(ManagementTenantExportJobEntity row, CancellationToken ct = default)
    {
        try { await dedicatedTable.AddEntityAsync(Encode(row), ct); return true; }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
    }
    public async Task<ManagementTenantExportJobEntity> ReplaceAsync(ManagementTenantExportJobEntity row, string expectedVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion) || expectedVersion.Trim() == "*") throw new ManagementOperationConflictException();
        try
        {
            var result = await dedicatedTable.UpdateEntityAsync(Encode(row), new ETag(expectedVersion), TableUpdateMode.Replace, ct);
            return row with { Version = result.Headers.ETag?.ToString() ?? throw new ManagementOperationUnavailableException() };
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new ManagementOperationConflictException(); }
    }
    public async Task<ManagementTenantExportJobPage> ListPageAsync(ManagementTenantExportDeployment deployment, int pageSize, string? continuationToken, CancellationToken ct = default)
    {
        if (pageSize is < 1 or > 25 || continuationToken?.Length > 4096 || (deployment.OrganizationId == Guid.Empty) != (deployment.ApplicationId == Guid.Empty)) throw new ManagementOperationInvalidException();
        var scope = new ManagementTenantExportExecution(new(deployment.OrganizationId, deployment.ApplicationId, deployment.RealmId, Guid.Empty, Guid.Empty, Guid.Empty), deployment.ApplicationKey, deployment.Environment, Guid.Empty, "", 0);
        var filter = deployment.OrganizationId == Guid.Empty ? TableClient.CreateQueryFilter($"ApplicationKey eq {deployment.ApplicationKey} and Environment eq {deployment.Environment} and RealmId eq {deployment.RealmId}") :
            TableClient.CreateQueryFilter($"PartitionKey eq {Partition(scope)}");
        var pages = dedicatedTable.QueryAsync<TableEntity>(filter, pageSize, ["PartitionKey", "RowKey", "Metadata"], ct).AsPages(continuationToken, pageSize);
        await foreach (var page in pages.WithCancellation(ct))
        {
            var handles = new List<ManagementTenantExportExecution>();
            foreach (var entity in page.Values)
            {
                var row = JsonSerializer.Deserialize<ManagementTenantExportJobEntity>(entity.GetString("Metadata")) ?? throw new ManagementOperationUnavailableException();
                if (Partition(row.Execution) != entity.PartitionKey || Row(row.Execution) != entity.RowKey || row.Execution.ApplicationKey != deployment.ApplicationKey ||
                    row.Execution.Environment != deployment.Environment || row.Execution.Authority.RealmId != deployment.RealmId ||
                    deployment.OrganizationId != Guid.Empty && (row.Execution.Authority.OrganizationId != deployment.OrganizationId || row.Execution.Authority.ApplicationId != deployment.ApplicationId))
                    throw new ManagementOperationUnavailableException();
                handles.Add(row.Execution);
            }
            return new(handles.AsReadOnly(), page.ContinuationToken);
        }
        return new([], null);
    }
}
public static class ManagementTenantExportStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableTenantExportJobs(this IServiceCollection services, TableClient dedicatedTable)
    {
        ArgumentNullException.ThrowIfNull(dedicatedTable);
        services.TryAddScoped<IManagementTenantExportJobRepository>(_ => new AzureTableManagementTenantExportJobRepository(dedicatedTable));
        return services;
    }
}
