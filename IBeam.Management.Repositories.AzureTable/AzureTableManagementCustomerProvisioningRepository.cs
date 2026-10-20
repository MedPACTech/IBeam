using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementCustomerProvisioningRepository(TableClient table) : IManagementCustomerProvisioningRepository
{
    public async Task LinkOperationAsync(ManagementTenantAdministrationScope scope, Guid tenant, Guid operation, Guid invite, CancellationToken ct = default)
    {
        var row = new TableEntity(Partition(scope, tenant), "OP|" + operation.ToString("N")) { ["InviteId"] = invite };
        try { await table.AddEntityAsync(row, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) {
            var current = await table.GetEntityAsync<TableEntity>(row.PartitionKey, row.RowKey, cancellationToken: ct);
            if (current.Value.GetGuid("InviteId") != invite) throw new ManagementIdentityOperationConflictException();
        }
    }
    public async Task<ManagementCustomerProvisioningEntity?> GetForOperationAsync(ManagementTenantAdministrationScope scope, Guid tenant, Guid operation, CancellationToken ct = default)
    {
        var row = await table.GetEntityIfExistsAsync<TableEntity>(Partition(scope, tenant), "OP|" + operation.ToString("N"), cancellationToken: ct);
        return row.HasValue && row.Value.GetGuid("InviteId") is Guid invite ? await GetAsync(scope, tenant, invite, ct) : null;
    }
    private static string Partition(ManagementTenantAdministrationScope scope, Guid tenant) => $"{scope.IdentityRealmId:N}|{tenant:N}|" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { scope.ApplicationKey, scope.Environment }))));
    private sealed record PrivateState(string Fingerprint, string EncryptedInput);
    private static TableEntity Encode(ManagementCustomerProvisioningEntity row) => new(Partition(row.Scope, row.TenantId), row.InviteId.ToString("N")) {
        ["Metadata"] = JsonSerializer.Serialize(row with { Version = "" }), ["Internal"] = JsonSerializer.Serialize(new PrivateState(row.Fingerprint, row.EncryptedInput)) };
    public async Task<ManagementCustomerProvisioningEntity?> GetAsync(ManagementTenantAdministrationScope scope, Guid tenant, Guid id, CancellationToken ct = default)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition(scope, tenant), id.ToString("N"), cancellationToken: ct);
        if (!response.HasValue) return null;
        var row = JsonSerializer.Deserialize<ManagementCustomerProvisioningEntity>(response.Value.GetString("Metadata")) ?? throw new InvalidOperationException("Invalid provisioning journal.");
        var state = JsonSerializer.Deserialize<PrivateState>(response.Value.GetString("Internal")) ?? throw new InvalidOperationException("Invalid provisioning journal.");
        if (row.Scope != scope || row.TenantId != tenant || row.InviteId != id) throw new InvalidOperationException("Invalid provisioning scope.");
        return row with { Fingerprint = state.Fingerprint, EncryptedInput = state.EncryptedInput, Version = response.Value.ETag.ToString() };
    }
    public async Task<bool> TryCreateAsync(ManagementCustomerProvisioningEntity row, CancellationToken ct = default)
    { try { await table.AddEntityAsync(Encode(row), ct); return true; } catch (RequestFailedException ex) when (ex.Status == 409) { return false; } }
    public async Task<ManagementCustomerProvisioningEntity> ReplaceAsync(ManagementCustomerProvisioningEntity row, string version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version) || version == "*") throw new ManagementIdentityOperationConflictException();
        try { var response = await table.UpdateEntityAsync(Encode(row), new ETag(version), TableUpdateMode.Replace, ct);
            return row with { Version = response.Headers.ETag?.ToString() ?? throw new InvalidOperationException("Provisioning version unavailable.") }; }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new ManagementIdentityOperationConflictException(); }
    }
}
public static class ManagementCustomerProvisioningStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableCustomerProvisioningStore(this IServiceCollection services, TableClient dedicatedJournal)
    { ArgumentNullException.ThrowIfNull(dedicatedJournal); services.TryAddScoped<IManagementCustomerProvisioningRepository>(_ => new AzureTableManagementCustomerProvisioningRepository(dedicatedJournal)); return services; }
}
