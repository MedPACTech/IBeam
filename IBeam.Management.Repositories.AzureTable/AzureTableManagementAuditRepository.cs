using Azure.Data.Tables;
using IBeam.Management.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;
/// <summary>Append-only writer to a separately provisioned table. No list/update/delete or runtime schema creation.</summary>
public sealed class AzureTableManagementAuditRepository(TableClient table) : IManagementAuditRepository
{
    public async Task AppendAsync(ManagementAuditEntity x, CancellationToken ct = default)
    {
        var partition = (x.OrganizationId?.ToString("N") ?? "DEPLOYMENT") + "|" + x.OccurredUtc.UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var row = new TableEntity(partition, x.AuditId.ToString("N")) {
            ["AuditId"] = x.AuditId, ["OccurredUtc"] = x.OccurredUtc, ["ExpiresUtc"] = x.ExpiresUtc,
            ["OrganizationId"] = x.OrganizationId, ["ActorUserId"] = x.ActorUserId, ["CredentialId"] = x.CredentialId,
            ["ApplicationId"] = x.ApplicationId, ["SubjectId"] = x.SubjectId, ["SubjectType"] = x.SubjectType,
            ["ApplicationKey"] = x.ApplicationKey, ["Environment"] = x.Environment, ["Operation"] = x.Operation,
            ["Succeeded"] = x.Succeeded, ["CorrelationId"] = x.CorrelationId };
        await table.AddEntityAsync(row, ct);
    }
}
public static class ManagementAuditStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableAuditStore(this IServiceCollection services, TableClient dedicatedAuditTable)
    {
        ArgumentNullException.ThrowIfNull(dedicatedAuditTable);
        services.TryAddScoped<IManagementAuditRepository>(_ => new AzureTableManagementAuditRepository(dedicatedAuditTable)); return services;
    }
}
