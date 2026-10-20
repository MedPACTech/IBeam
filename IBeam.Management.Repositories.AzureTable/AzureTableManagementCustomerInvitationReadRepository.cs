using System.Security.Cryptography;
using System.Text;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementCustomerInvitationReadRepository(TableClient table) : IManagementCustomerInvitationReadRepository
{
    private static readonly string[] Columns = ["PartitionKey", "RowKey", "Timestamp", "TenantId", "InviteId", "DestinationType", "NormalizedDestination",
        "FirstName", "LastName", "Status", "RoleIdsCsv", "RoleNamesJson", "CreatedUtc", "ExpiresUtc", "SentUtc", "RedeemedUtc", "RevokedUtc"];
    public string SourceId { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(table.Uri.GetLeftPart(UriPartial.Path))));
    public async Task<ManagementCustomerReadPage<ManagementCustomerInvitationReadEntity>> ReadPageAsync(Guid tenantId, int pageSize, string? continuationToken, CancellationToken ct = default)
    {
        Validate(tenantId, pageSize, continuationToken);
        var partition = "TEN|" + tenantId.ToString("D");
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}") + " and RowKey ge 'INV|' and RowKey lt 'INV}'";
        await foreach (var page in table.QueryAsync<TableEntity>(filter, pageSize, Columns, ct).AsPages(continuationToken, pageSize))
            return new(page.Values.Select(x => Map(x, tenantId)).Where(x => x is not null).Cast<ManagementCustomerInvitationReadEntity>().ToArray(), page.ContinuationToken);
        return new([], null);
    }
    public async Task<ManagementCustomerInvitationReadEntity?> FindAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default)
    {
        Validate(tenantId, 1, null);
        if (inviteId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        var row = await table.GetEntityIfExistsAsync<TableEntity>("TEN|" + tenantId.ToString("D"), "INV|" + inviteId.ToString("D"), Columns, ct);
        return row.HasValue ? Map(row.Value!, tenantId) : null;
    }
    private static ManagementCustomerInvitationReadEntity? Map(TableEntity row, Guid tenantId)
    {
        try
        {
            if (!row.RowKey.StartsWith("INV|", StringComparison.Ordinal) || !Guid.TryParseExact(row.RowKey[4..], "D", out var id) || id == Guid.Empty ||
                row.GetString("TenantId") is not { } tenant || !Guid.TryParse(tenant, out var actualTenant) || actualTenant != tenantId ||
                row.GetString("InviteId") is not { } invite || !Guid.TryParse(invite, out var actualInvite) || actualInvite != id) return null;
            return new(tenantId, id, row.GetString("DestinationType"), row.GetString("NormalizedDestination"), row.GetString("FirstName"), row.GetString("LastName"),
                row.GetString("Status"), row.GetString("RoleIdsCsv"), row.GetString("RoleNamesJson"), row.GetDateTimeOffset("CreatedUtc"), row.GetDateTimeOffset("ExpiresUtc"),
                row.GetDateTimeOffset("SentUtc"), row.GetDateTimeOffset("RedeemedUtc"), row.GetDateTimeOffset("RevokedUtc"), row.ETag.ToString()) { ModifiedUtc = row.Timestamp };
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or ArgumentException) { return null; }
    }
    private static void Validate(Guid tenant, int size, string? cursor)
    {
        if (tenant == Guid.Empty || size is < 1 or > 100 || cursor?.Length > 12000) throw new ManagementCustomerIdentityInvalidRequestException();
    }
}
public static class ManagementCustomerInvitationReaderStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableCustomerInvitationReader(this IServiceCollection services, TableClient customerTenantInvitesTable)
    {
        ArgumentNullException.ThrowIfNull(customerTenantInvitesTable);
        services.TryAddScoped<IManagementCustomerInvitationReadRepository>(_ => new AzureTableManagementCustomerInvitationReadRepository(customerTenantInvitesTable));
        return services;
    }
}
