using System.Security.Cryptography;
using System.Text;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

/// <summary>Existing Identity TEN partition only; explicit metadata projection and one bounded provider page. Never creates a schema.</summary>
public sealed class AzureTableManagementCustomerTenantReadRepository(TableClient tenantsTable) : IManagementCustomerTenantReadRepository
{
    private static readonly string[] Columns = ["PartitionKey", "RowKey", "Name", "NormalizedName", "Status", "CreatedAt", "UpdatedAt"];
    public string SourceId { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenantsTable.Uri.GetLeftPart(UriPartial.Path))));
    public async Task<ManagementCustomerTenantReadPage> ReadPageAsync(int pageSize, string? continuationToken, CancellationToken ct = default)
    {
        if (pageSize is < 1 or > 100 || continuationToken?.Length > 12000) throw new ManagementCustomerIdentityInvalidRequestException();
        await foreach (var page in tenantsTable.QueryAsync<TableEntity>("PartitionKey eq 'TEN'", pageSize, Columns, ct).AsPages(continuationToken, pageSize))
            return new(page.Values.Select(Map).Where(x => x is not null).Cast<ManagementCustomerTenantReadEntity>().ToArray(), page.ContinuationToken);
        return new([], null);
    }
    public async Task<ManagementCustomerTenantReadEntity?> FindAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        var result = await tenantsTable.GetEntityIfExistsAsync<TableEntity>("TEN", tenantId.ToString("D"), Columns, ct);
        return result.HasValue ? Map(result.Value) : null;
    }
    private static ManagementCustomerTenantReadEntity? Map(TableEntity row)
    {
        try { return new(row.RowKey, row.GetString("Name"), row.GetString("NormalizedName"), row.GetString("Status"),
            row.GetDateTimeOffset("CreatedAt"), row.GetDateTimeOffset("UpdatedAt"), row.ETag.ToString()); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or ArgumentException) { return null; }
    }
}
public static class ManagementCustomerTenantReaderStorageExtensions
{
    /// <summary>Supply this target's existing dedicated Identity tenants table, optionally via read-only SAS. No portal/global TableClient is changed.</summary>
    public static IServiceCollection AddIBeamManagementAzureTableCustomerTenantReader(this IServiceCollection services, TableClient dedicatedCustomerTenantsTable)
    {
        ArgumentNullException.ThrowIfNull(dedicatedCustomerTenantsTable);
        services.TryAddScoped<IManagementCustomerTenantReadRepository>(_ => new AzureTableManagementCustomerTenantReadRepository(dedicatedCustomerTenantsTable));
        return services;
    }
}
