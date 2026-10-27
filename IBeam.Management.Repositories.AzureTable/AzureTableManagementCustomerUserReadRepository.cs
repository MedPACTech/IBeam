using System.Security.Cryptography;
using System.Text;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementCustomerUserReadRepository(TableClient table) : IManagementCustomerUserReadRepository
{
    private static readonly string[] Columns = ["PartitionKey", "RowKey", "Timestamp", "TenantId", "UserId", "Email", "Status", "RolesCsv", "RoleIdsCsv", "CreatedAt", "DisabledAt"];
    public string SourceId { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(table.Uri.GetLeftPart(UriPartial.Path))));
    public async Task<ManagementCustomerReadPage<ManagementCustomerUserReadEntity>> ReadPageAsync(Guid tenantId, int pageSize, string? continuationToken, CancellationToken ct = default)
    {
        Validate(tenantId, pageSize, continuationToken);
        var partition = "TEN|" + tenantId.ToString("D");
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}") + " and RowKey ge 'USR|' and RowKey lt 'USR}'";
        await foreach (var page in table.QueryAsync<TableEntity>(filter, pageSize, Columns, ct).AsPages(continuationToken, pageSize))
            return new(page.Values.Select(x => Map(x, tenantId)).Where(x => x is not null).Cast<ManagementCustomerUserReadEntity>().ToArray(), page.ContinuationToken);
        return new([], null);
    }
    public async Task<ManagementCustomerUserReadEntity?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        Validate(tenantId, 1, null);
        if (userId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        var row = await table.GetEntityIfExistsAsync<TableEntity>("TEN|" + tenantId.ToString("D"), "USR|" + userId.ToString("D"), Columns, ct);
        return row.HasValue ? Map(row.Value!, tenantId) : null;
    }
    private static ManagementCustomerUserReadEntity? Map(TableEntity row, Guid tenantId)
    {
        try
        {
            if (!row.RowKey.StartsWith("USR|", StringComparison.Ordinal) || !Guid.TryParseExact(row.RowKey[4..], "D", out var id) || id == Guid.Empty ||
                row.GetString("TenantId") is { Length: > 0 } tenant && (!Guid.TryParse(tenant, out var actualTenant) || actualTenant != tenantId) ||
                row.GetString("UserId") is { Length: > 0 } user && (!Guid.TryParse(user, out var actualUser) || actualUser != id)) return null;
            return new(tenantId, id, row.GetString("Email"), row.GetString("Status"), row.GetString("RolesCsv"), row.GetString("RoleIdsCsv"),
                row.GetDateTimeOffset("CreatedAt"), row.GetDateTimeOffset("DisabledAt"), row.ETag.ToString()) { ModifiedUtc = row.Timestamp };
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or ArgumentException) { return null; }
    }
    private static void Validate(Guid tenant, int size, string? cursor)
    {
        if (tenant == Guid.Empty || size is < 1 or > 100 || cursor?.Length > 12000) throw new ManagementCustomerIdentityInvalidRequestException();
    }
}
public static class ManagementCustomerUserReaderStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableCustomerUserReader(this IServiceCollection services, TableClient customerTenantUsersTable)
    {
        ArgumentNullException.ThrowIfNull(customerTenantUsersTable);
        services.TryAddScoped<IManagementCustomerUserReadRepository>(_ => new AzureTableManagementCustomerUserReadRepository(customerTenantUsersTable));
        return services;
    }
}
