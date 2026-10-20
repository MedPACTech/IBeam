using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

/// <summary>One bounded projected page from the explicit target Identity role table; no permission/assignment/profile columns.</summary>
public sealed class AzureTableManagementAssignableRoleReadRepository(TableClient table) : IManagementAssignableRoleReadRepository
{
    public async Task<IReadOnlyList<ManagementAssignableRoleReadEntity>> ReadAsync(Guid tenantId, IReadOnlyList<string> names, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || names is not { Count: > 0 and <= 32 } || names.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100 || x.Any(char.IsControl)))
            throw new ManagementCustomerIdentityInvalidRequestException();
        var partition = "TEN|" + tenantId.ToString("D");
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}") + " and RowKey ge 'ROL|' and RowKey lt 'ROL}' and (" +
            string.Join(" or ", names.Select(name => TableClient.CreateQueryFilter($"Name eq {name}"))) + ")";
        await foreach (var page in table.QueryAsync<TableEntity>(filter, 33, ["PartitionKey", "RowKey", "TenantId", "RoleId", "Name", "Status"], ct).AsPages(pageSizeHint: 33)) {
            if (page.Values.Count > 32 || page.ContinuationToken is not null) throw new ManagementCustomerIdentityUnavailableException();
            var result = new List<ManagementAssignableRoleReadEntity>();
            foreach (var row in page.Values) {
                if (row.PartitionKey != partition || !row.RowKey.StartsWith("ROL|", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(row.RowKey[4..], "D", out var id) || id == Guid.Empty ||
                    row.GetString("TenantId") != tenantId.ToString("D") || row.GetString("RoleId") != id.ToString("D") ||
                    row.GetString("Name") is not { } name || !names.Contains(name, StringComparer.Ordinal)) throw new ManagementCustomerIdentityUnavailableException();
                result.Add(new(tenantId, id, name, string.Equals(row.GetString("Status"), "Active", StringComparison.OrdinalIgnoreCase)));
            }
            return result;
        }
        return [];
    }
}
public static class ManagementAssignableRoleStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableAssignableRoles(this IServiceCollection services, TableClient explicitIdentityRolesTable)
    { ArgumentNullException.ThrowIfNull(explicitIdentityRolesTable); services.TryAddScoped<IManagementAssignableRoleReadRepository>(_ => new AzureTableManagementAssignableRoleReadRepository(explicitIdentityRolesTable)); return services; }
}
