using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Repositories.AzureTable;

/// <summary>One bounded private journal row per original intent; the host preprovisions its dedicated table.</summary>
public sealed class AzureTableManagementBulkInvitationBatchRepository(TableClient table) : IManagementBulkInvitationBatchRepository
{
    public int ProtocolVersion => 1; public bool Durable => true;
    private static string Partition(ManagementOperationContext scope) => "B|" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        scope.OrganizationId, scope.ApplicationId, scope.RealmId, scope.TenantId })));
    private static TableEntity Encode(ManagementBulkInvitationBatch row)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(row with { Version = "" });
        if (bytes.Length > 120000) throw new ManagementOperationInvalidException();
        return new(Partition(row.Scope), row.Scope.OperationId.ToString("D")) {
            ["Data0"] = bytes[..Math.Min(60000, bytes.Length)], ["Data1"] = bytes[Math.Min(60000, bytes.Length)..] };
    }
    public async Task<ManagementBulkInvitationBatch?> GetAsync(ManagementOperationContext scope, CancellationToken ct = default)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition(scope), scope.OperationId.ToString("D"), cancellationToken: ct);
        if (!response.HasValue) return null;
        var first = response.Value.GetBinaryData("Data0")?.ToArray() ?? throw new ManagementOperationUnavailableException();
        var second = response.Value.GetBinaryData("Data1")?.ToArray() ?? throw new ManagementOperationUnavailableException();
        if (first.Length > 60000 || second.Length > 60000) throw new ManagementOperationUnavailableException();
        var row = JsonSerializer.Deserialize<ManagementBulkInvitationBatch>(first.Concat(second).ToArray()) ?? throw new ManagementOperationUnavailableException();
        if ((row.Scope with { ResourceVersion = null }) != (scope with { ResourceVersion = null })) throw new ManagementOperationConflictException();
        return row with { Version = response.Value.ETag.ToString() };
    }
    public async Task<bool> TryCreateAsync(ManagementBulkInvitationBatch row, CancellationToken ct = default)
    { try { await table.AddEntityAsync(Encode(row), ct); return true; } catch (RequestFailedException ex) when (ex.Status == 409) { return false; } }
    public async Task<ManagementBulkInvitationBatch> ReplaceAsync(ManagementBulkInvitationBatch row, string expectedVersion, CancellationToken ct = default)
    {
        if (!ManagementBulkInvitationRevokeOperation.Exact(expectedVersion)) throw new ManagementOperationConflictException();
        try {
            var response = await table.UpdateEntityAsync(Encode(row), new ETag(expectedVersion), TableUpdateMode.Replace, ct);
            return row with { Version = response.Headers.ETag?.ToString() ?? throw new ManagementOperationUnavailableException() };
        } catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new ManagementOperationConflictException(); }
    }
}
public static class ManagementBulkInvitationBatchStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableBulkInvitationBatchStore(this IServiceCollection services, TableClient dedicatedJournal)
    { ArgumentNullException.ThrowIfNull(dedicatedJournal); services.TryAddScoped<IManagementBulkInvitationBatchRepository>(_ => new AzureTableManagementBulkInvitationBatchRepository(dedicatedJournal)); return services; }
}
