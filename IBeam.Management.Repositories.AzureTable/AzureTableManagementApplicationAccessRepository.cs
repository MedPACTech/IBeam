using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementApplicationAccessRepository(TableClient table) : IManagementApplicationAccessRepository
{
    private static string Partition(Guid org, Guid app) => "ORG|" + org.ToString("N") + "|APP|" + app.ToString("N");
    private static string Row(Guid user) => "USER|" + user.ToString("N");
    public async Task<ManagementApplicationAccessEntity?> GetAsync(Guid organizationId, Guid applicationId, Guid userId, CancellationToken ct = default)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition(organizationId, applicationId), Row(userId), cancellationToken: ct);
        return response.HasValue ? Decode(response.Value) : null;
    }
    public async Task<ManagementApplicationAccessPage> ListAsync(Guid organizationId, Guid applicationId, Guid? afterUserId, int pageSize, CancellationToken ct = default)
    {
        if (pageSize is < 1 or > 100) throw new ManagementApplicationInvalidException();
        var partition = Partition(organizationId, applicationId); var lower = afterUserId.HasValue ? Row(afterUserId.Value) : "USER|"; var upper = "USER}";
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and RowKey gt {lower} and RowKey lt {upper}");
        await foreach (var page in table.QueryAsync<TableEntity>(filter, pageSize, cancellationToken: ct).AsPages(pageSizeHint: pageSize))
        {
            var rows = page.Values.Select(Decode).ToArray();
            if (rows.Length == 0 && page.ContinuationToken is not null) throw new ManagementApplicationUnavailableException();
            return new(rows, page.ContinuationToken is null ? null : rows[^1].UserId);
        }
        return new([], null);
    }
    public async Task<ManagementApplicationAccessEntity> CreateAsync(ManagementApplicationAccessEntity row, CancellationToken ct = default)
    {
        try
        {
            var result = await table.AddEntityAsync(Encode(row), ct);
            return row with { Version = result.Headers.ETag?.ToString() ?? "" };
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { throw new ManagementApplicationConflictException(); }
    }
    public async Task<ManagementApplicationAccessEntity> ReplaceAsync(ManagementApplicationAccessEntity row, string expectedVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion) || expectedVersion == "*") throw new ManagementApplicationInvalidException();
        try
        {
            var result = await table.UpdateEntityAsync(Encode(row), new ETag(expectedVersion), TableUpdateMode.Replace, ct);
            return row with { Version = result.Headers.ETag?.ToString() ?? "" };
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new ManagementApplicationConflictException(); }
    }
    private static TableEntity Encode(ManagementApplicationAccessEntity x) => new(Partition(x.OrganizationId, x.ApplicationId), Row(x.UserId))
    {
        ["OrganizationId"] = x.OrganizationId, ["ApplicationId"] = x.ApplicationId, ["UserId"] = x.UserId,
        ["Permissions"] = JsonSerializer.Serialize(x.Permissions), ["CreatedUtc"] = x.CreatedUtc, ["UpdatedUtc"] = x.UpdatedUtc,
        ["UpdatedBy"] = x.UpdatedBy, ["RevokedUtc"] = x.RevokedUtc, ["RevokedBy"] = x.RevokedBy
    };
    private static ManagementApplicationAccessEntity Decode(TableEntity x) => new()
    {
        OrganizationId = x.GetGuid("OrganizationId")!.Value, ApplicationId = x.GetGuid("ApplicationId")!.Value,
        UserId = x.GetGuid("UserId")!.Value, Permissions = JsonSerializer.Deserialize<string[]>(x.GetString("Permissions")) ?? [],
        CreatedUtc = x.GetDateTimeOffset("CreatedUtc")!.Value, UpdatedUtc = x.GetDateTimeOffset("UpdatedUtc")!.Value,
        UpdatedBy = x.GetGuid("UpdatedBy")!.Value, RevokedUtc = x.GetDateTimeOffset("RevokedUtc"), RevokedBy = x.GetGuid("RevokedBy"), Version = x.ETag.ToString()
    };
}
public static class ManagementApplicationAccessStorageExtensions
{
    /// <summary>Explicitly provision ManagementApplicationAccess first; supply a dedicated management table client.</summary>
    public static IServiceCollection AddIBeamManagementAzureTableApplicationAccessStore(this IServiceCollection services, TableClient accessTable)
    {
        ArgumentNullException.ThrowIfNull(accessTable);
        services.TryAddScoped<IManagementApplicationAccessRepository>(_ => new AzureTableManagementApplicationAccessRepository(accessTable));
        return services;
    }
}
