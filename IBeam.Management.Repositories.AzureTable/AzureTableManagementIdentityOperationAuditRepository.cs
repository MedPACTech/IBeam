using Azure;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

/// <summary>Idempotent append-only outbox destination. Duplicate IDs with different metadata fail.</summary>
public sealed class AzureTableManagementIdentityOperationAuditRepository(TableClient table) : IManagementIdentityOperationAuditRepository
{
    public async Task AppendOnceAsync(ManagementAuditEntity x, CancellationToken ct = default)
    {
        var partition = x.OrganizationId?.ToString("N") + "|" + x.OccurredUtc.UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var row = new TableEntity(partition, x.AuditId.ToString("N")) {
            ["AuditId"] = x.AuditId, ["OccurredUtc"] = x.OccurredUtc, ["ExpiresUtc"] = x.ExpiresUtc,
            ["OrganizationId"] = x.OrganizationId, ["ActorUserId"] = x.ActorUserId, ["CredentialId"] = x.CredentialId,
            ["ApplicationId"] = x.ApplicationId, ["SubjectId"] = x.SubjectId, ["SubjectType"] = x.SubjectType,
            ["ApplicationKey"] = x.ApplicationKey, ["Environment"] = x.Environment, ["Operation"] = x.Operation,
            ["Succeeded"] = x.Succeeded, ["CorrelationId"] = x.CorrelationId };
        try { await table.AddEntityAsync(row, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) {
            var existing = (await table.GetEntityAsync<TableEntity>(partition, row.RowKey, cancellationToken: ct)).Value;
            foreach (var field in row) {
                existing.TryGetValue(field.Key, out var value);
                if (!Equals(value, field.Value)) throw new InvalidOperationException("Mutation audit identity conflict.");
            }
        }
    }
}
public static class ManagementIdentityOperationAuditStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableIdentityOperationAuditStore(this IServiceCollection services, TableClient dedicatedAuditTable)
    {
        ArgumentNullException.ThrowIfNull(dedicatedAuditTable);
        services.TryAddScoped<IManagementIdentityOperationAuditRepository>(_ => new AzureTableManagementIdentityOperationAuditRepository(dedicatedAuditTable));
        return services;
    }
}
