using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementCustomerInvitationTransitionRepository(TableClient table) : IManagementCustomerInvitationTransitionRepository
{
    private static string Partition(ManagementTenantAdministrationScope scope, Guid tenant) => $"{scope.IdentityRealmId:N}|{tenant:N}|" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { scope.ApplicationKey, scope.Environment }))));
    private static TableEntity Encode(ManagementCustomerInvitationTransitionEntity row) => new(Partition(row.Scope, row.TenantId), row.OperationId.ToString("N")) {
        ["Metadata"] = JsonSerializer.Serialize(row with { Version = "" }), ["EncryptedInput"] = row.EncryptedInput };
    public async Task<ManagementCustomerInvitationTransitionEntity?> GetAsync(ManagementTenantAdministrationScope scope, Guid tenant, Guid operation, CancellationToken ct = default)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition(scope, tenant), operation.ToString("N"), cancellationToken: ct);
        if (!response.HasValue) return null;
        var row = JsonSerializer.Deserialize<ManagementCustomerInvitationTransitionEntity>(response.Value.GetString("Metadata")) ?? throw new InvalidOperationException("Invalid invitation journal.");
        if (row.Scope != scope || row.TenantId != tenant || row.OperationId != operation) throw new InvalidOperationException("Invalid invitation journal scope.");
        return row with { EncryptedInput = response.Value.GetString("EncryptedInput"), Version = response.Value.ETag.ToString() };
    }
    public async Task<bool> TryCreateAsync(ManagementCustomerInvitationTransitionEntity row, CancellationToken ct = default)
    { try { await table.AddEntityAsync(Encode(row), ct); return true; } catch (RequestFailedException ex) when (ex.Status == 409) { return false; } }
    public async Task<ManagementCustomerInvitationTransitionEntity> ReplaceAsync(ManagementCustomerInvitationTransitionEntity row, string version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version) || version == "*") throw new ManagementIdentityOperationConflictException();
        try { var response = await table.UpdateEntityAsync(Encode(row), new ETag(version), TableUpdateMode.Replace, ct);
            return row with { Version = response.Headers.ETag?.ToString() ?? throw new InvalidOperationException("Invitation journal version unavailable.") }; }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new ManagementIdentityOperationConflictException(); }
    }
}
public static class ManagementCustomerInvitationTransitionStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableCustomerInvitationTransitionStore(this IServiceCollection services, TableClient dedicatedJournal)
    { ArgumentNullException.ThrowIfNull(dedicatedJournal); services.TryAddScoped<IManagementCustomerInvitationTransitionRepository>(_ => new AzureTableManagementCustomerInvitationTransitionRepository(dedicatedJournal)); return services; }
}
