using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

/// <summary>Atomic single-use assertions in a dedicated explicitly provisioned table. No runtime schema creation.</summary>
public sealed class AzureTableManagementDelegationReplayStore(TableClient table) : IManagementDelegationReplayStore
{
    public async Task<bool> TryClaimAsync(Guid realmId, string issuer, Guid assertionId, DateTimeOffset expiresUtc, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (realmId == Guid.Empty || assertionId == Guid.Empty || string.IsNullOrWhiteSpace(issuer) || issuer.Length > 200 ||
            expiresUtc <= now || expiresUtc > now.AddSeconds(60)) throw new ManagementAccessDeniedException();
        var issuerHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issuer)));
        try
        {
            await table.AddEntityAsync(new TableEntity(realmId.ToString("N"), issuerHash + "|" + assertionId.ToString("N")) {
                ["ExpiresUtc"] = expiresUtc, ["RealmId"] = realmId }, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
        catch (OperationCanceledException) { throw; }
        catch { throw new ManagementConnectionUnavailableException(); }
    }
    /// <summary>Trusted maintenance only. Bounded cleanup never removes live nonces; Azure Table has no automatic TTL.</summary>
    public async Task<int> PurgeExpiredAsync(Guid realmId, DateTimeOffset cutoff, int maximumRows = 100, CancellationToken ct = default)
    {
        if (realmId == Guid.Empty || cutoff > DateTimeOffset.UtcNow.AddMinutes(-1) || maximumRows is < 1 or > 1000)
            throw new ManagementAccessDeniedException();
        var partition = realmId.ToString("N");
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and ExpiresUtc le {cutoff}");
        var count = 0;
        await foreach (var page in table.QueryAsync<TableEntity>(filter, maximumRows, ["PartitionKey", "RowKey", "ExpiresUtc"], ct).AsPages())
        {
            foreach (var row in page.Values.Take(maximumRows - count))
            {
                await table.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, ct); count++;
            }
            break; // one provider page per maintenance call; repeated invocations continue draining expired rows.
        }
        return count;
    }
}
public static class ManagementDelegationReplayStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableDelegationReplayStore(this IServiceCollection services, TableClient dedicatedReplayTable)
    {
        ArgumentNullException.ThrowIfNull(dedicatedReplayTable);
        services.TryAddScoped<IManagementDelegationReplayStore>(_ => new AzureTableManagementDelegationReplayStore(dedicatedReplayTable));
        return services;
    }
}
