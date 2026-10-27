using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed partial class AzureTableTenantRoleStore : IIdentityTenantOwnershipRoleStore
{
    public int OwnershipProtocolVersion => 1;
    public bool RequiresOwnershipProtection => _opts.RequireOwnershipProtection;
    public async Task<IReadOnlyList<TenantRole>> ReadOwnershipRolesAsync(Guid tenant, IReadOnlyList<string> names, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty || names is not { Count: > 0 and <= 8 }) throw new IdentityValidationException("Ownership policy is invalid.");
        var rows = new List<TenantRole>();
        foreach (var name in names) {
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {_opts.TenantRolesPk(tenant)} and NormalizedName eq {name.ToUpperInvariant()}");
            var count = 0;
            await foreach (var row in RolesTable().QueryAsync<TenantRoleEntity>(filter, 2,
                ["PartitionKey", "RowKey", "TenantId", "RoleId", "Name", "IsSystem", "Status"], ct)) {
                if (++count > 1 || rows.Count >= 8) throw new IdentityValidationException("Ownership role definitions are ambiguous.");
                if (!Guid.TryParseExact(row.RoleId, "D", out var id) || row.PartitionKey != _opts.TenantRolesPk(tenant) || row.RowKey != _opts.TenantRolesRk(id))
                    throw new IdentityProviderException("Ownership role definition scope is invalid.");
                rows.Add(Map(row));
            }
        }
        return rows;
    }
}
