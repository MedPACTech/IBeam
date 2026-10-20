using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

/// <summary>Scoped point reads, atomic reservation and ETag claims. Provision the dedicated table explicitly.</summary>
public sealed class AzureTableManagementIdentityOperationRepository(TableClient table) : IManagementIdentityOperationRepository
{
    // Internal-only persistence includes the HMAC fingerprint and claim, never arbitrary request JSON.
    private sealed record PrivateState(string Fingerprint, Guid? LeaseId);
    private static string Partition(ManagementIdentityOperationScope s) => s.IdentityRealmId.ToString("N") + "|" + s.OrganizationId.ToString("N") + "|" + s.ApplicationId.ToString("N");
    private static ManagementIdentityOperationScope Scope(ManagementIdentityOperationEntity x) => new(x.IdentityRealmId, x.OrganizationId, x.ApplicationId);
    private static TableEntity Encode(ManagementIdentityOperationEntity x) => new(Partition(Scope(x)), x.OperationId.ToString("N")) {
        ["Metadata"] = JsonSerializer.Serialize(x with { Version = "" }),
        ["Internal"] = JsonSerializer.Serialize(new PrivateState(x.Fingerprint, x.LeaseId)),
        ["ExpiresUtc"] = x.ExpiresUtc };
    private static ManagementIdentityOperationEntity Decode(TableEntity row, ManagementIdentityOperationScope scope, Guid id)
    {
        var result = JsonSerializer.Deserialize<ManagementIdentityOperationEntity>(row.GetString("Metadata")) ?? throw new InvalidOperationException("Invalid operation metadata.");
        var state = JsonSerializer.Deserialize<PrivateState>(row.GetString("Internal")) ?? throw new InvalidOperationException("Invalid operation metadata.");
        if (Scope(result) != scope || result.OperationId != id || row.PartitionKey != Partition(scope) || row.RowKey != id.ToString("N"))
            throw new InvalidOperationException("Invalid operation scope.");
        return result with { Fingerprint = state.Fingerprint, LeaseId = state.LeaseId, Version = row.ETag.ToString() };
    }
    public async Task<ManagementIdentityOperationEntity?> GetAsync(ManagementIdentityOperationScope scope, Guid id, CancellationToken ct = default)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition(scope), id.ToString("N"), cancellationToken: ct);
        return response.HasValue ? Decode(response.Value, scope, id) : null;
    }
    public async Task<bool> TryCreateAsync(ManagementIdentityOperationEntity operation, CancellationToken ct = default)
    {
        try { await table.AddEntityAsync(Encode(operation), ct); return true; }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
    }
    public async Task<ManagementIdentityOperationEntity> ReplaceAsync(ManagementIdentityOperationEntity operation, string expectedVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion) || expectedVersion == "*") throw new ManagementIdentityOperationConflictException();
        try {
            var response = await table.UpdateEntityAsync(Encode(operation), new ETag(expectedVersion), TableUpdateMode.Replace, ct);
            return operation with { Version = response.Headers.ETag?.ToString() ?? throw new InvalidOperationException("Missing operation version.") };
        } catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412) { throw new ManagementIdentityOperationConflictException(); }
    }
}
public static class ManagementIdentityOperationStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableIdentityOperationStore(this IServiceCollection services, TableClient dedicatedOperationsTable)
    {
        ArgumentNullException.ThrowIfNull(dedicatedOperationsTable);
        services.TryAddScoped<IManagementIdentityOperationRepository>(_ => new AzureTableManagementIdentityOperationRepository(dedicatedOperationsTable));
        return services;
    }
}
