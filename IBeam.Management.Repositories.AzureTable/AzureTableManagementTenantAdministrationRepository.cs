using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

/// <summary>One tenant metadata entity with an atomic uniqueness index. No customer extension or membership data.</summary>
public sealed class AzureTableManagementTenantAdministrationRepository(TableClient table) : IManagementTenantAdministrationRepository
{
    private static string Partition(ManagementTenantAdministrationScope scope) => scope.IdentityRealmId.ToString("N") + "|" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { scope.ApplicationKey, scope.Environment }))));
    private static ManagementTenantAdministrationScope Scope(ManagementTenantAdministrationEntity row) => new(row.IdentityRealmId, row.ApplicationKey, row.Environment);
    private static TableEntity Encode(ManagementTenantAdministrationEntity row) => new(Partition(Scope(row)), "TEN|" + row.TenantId.ToString("N")) {
        ["Metadata"] = JsonSerializer.Serialize(row with { Version = "" }) };
    public async Task<ManagementTenantAdministrationEntity?> GetAsync(ManagementTenantAdministrationScope scope, Guid id, CancellationToken ct = default)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition(scope), "TEN|" + id.ToString("N"), cancellationToken: ct);
        if (!response.HasValue) return null;
        var row = JsonSerializer.Deserialize<ManagementTenantAdministrationEntity>(response.Value.GetString("Metadata"))
            ?? throw new InvalidOperationException("Tenant metadata is invalid.");
        if (Scope(row) != scope || row.TenantId != id) throw new InvalidOperationException("Tenant metadata scope is invalid.");
        return row with { Version = response.Value.ETag.ToString() };
    }
    public async Task<bool> TryCreateAsync(ManagementTenantAdministrationEntity entity, CancellationToken ct = default)
    {
        var row = Encode(entity);
        var batch = new List<TableTransactionAction> { new(TableTransactionActionType.Add, row) };
        if (entity.ExternalReference is not null) batch.Add(new(TableTransactionActionType.Add,
            new TableEntity(row.PartitionKey, "REF|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entity.ExternalReference.ToUpperInvariant())))) {
                ["TenantId"] = entity.TenantId }));
        try { await table.SubmitTransactionAsync(batch, ct); return true; }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
    }
    public async Task<ManagementTenantAdministrationEntity> ReplaceAsync(ManagementTenantAdministrationEntity entity, string version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version) || version == "*") throw new ManagementIdentityOperationConflictException();
        var old = await GetAsync(Scope(entity), entity.TenantId, ct) ?? throw new ManagementIdentityOperationConflictException();
        if (old.Version != version) throw new ManagementIdentityOperationConflictException();
        if (old.ExternalReference != entity.ExternalReference) throw new InvalidOperationException("External references are immutable.");
        try {
            var response = await table.UpdateEntityAsync(Encode(entity), new ETag(version), TableUpdateMode.Replace, ct);
            return entity with { Version = response.Headers.ETag?.ToString() ?? throw new InvalidOperationException("Tenant metadata version is unavailable.") };
        } catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new ManagementIdentityOperationConflictException(); }
    }
}
public static class ManagementTenantAdministrationStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableTenantAdministrationStore(this IServiceCollection services, TableClient dedicatedMetadataTable)
    {
        ArgumentNullException.ThrowIfNull(dedicatedMetadataTable);
        services.TryAddScoped<IManagementTenantAdministrationRepository>(_ => new AzureTableManagementTenantAdministrationRepository(dedicatedMetadataTable)); return services;
    }
}
