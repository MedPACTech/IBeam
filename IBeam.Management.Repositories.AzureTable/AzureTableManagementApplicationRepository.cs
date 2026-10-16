using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.Entities;

namespace IBeam.Management.Repositories.AzureTable;

/// <summary>Application rows and their uniqueness indexes share a transaction partition. No runtime schema creation.</summary>
public sealed class AzureTableManagementApplicationRepository(TableClient table) : IManagementApplicationRepository
{
    private static string Partition(Guid id) => "ORG|" + id.ToString("N");
    private static string Row(Guid id) => "APP|" + id.ToString("N");
    public async Task<ManagementApplicationEntity?> GetAsync(Guid organizationId, Guid applicationId, CancellationToken ct = default)
    {
        var result = await table.GetEntityIfExistsAsync<TableEntity>(Partition(organizationId), Row(applicationId), cancellationToken: ct);
        return result.HasValue ? Decode(result.Value) : null;
    }
    public async Task<ManagementApplicationPage> ListAsync(Guid organizationId, Guid? after, int pageSize, CancellationToken ct = default)
    {
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var partition = Partition(organizationId);
        var lower = after.HasValue ? Row(after.Value) : "APP|";
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and RowKey gt {lower} and RowKey lt {"APP}"}");
        await foreach (var page in table.QueryAsync<TableEntity>(filter, pageSize, cancellationToken: ct).AsPages(pageSizeHint: pageSize))
        {
            var items = page.Values.Select(Decode).ToArray();
            if (items.Length == 0 && page.ContinuationToken is not null) throw new InvalidOperationException("Application storage page did not advance.");
            return new(items, page.ContinuationToken is null ? null : items[^1].ApplicationId);
        }
        return new([], null);
    }
    public async Task<ManagementApplicationEntity> CreateAsync(ManagementApplicationEntity application, CancellationToken ct = default)
    {
        var row = Encode(application);
        var unique = "KEY|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { application.ApplicationKey.ToLowerInvariant(), application.Environment.ToLowerInvariant() }))));
        try
        {
            var response = await table.SubmitTransactionAsync([
                new TableTransactionAction(TableTransactionActionType.Add, row),
                new TableTransactionAction(TableTransactionActionType.Add, new TableEntity(row.PartitionKey, unique)
                    { ["ApplicationId"] = application.ApplicationId })], ct);
            return application with { Version = response.Value[0].Headers.ETag?.ToString() ?? row.ETag.ToString() };
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { throw new ManagementApplicationConflictException(); }
    }
    public async Task<ManagementApplicationEntity> ReplaceAsync(ManagementApplicationEntity application, string expectedVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion) || expectedVersion == "*") throw new ManagementApplicationInvalidException();
        try
        {
            var response = await table.UpdateEntityAsync(Encode(application), new ETag(expectedVersion), TableUpdateMode.Replace, ct);
            return application with { Version = response.Headers.ETag?.ToString() ?? "" };
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new ManagementApplicationConflictException(); }
    }
    private static TableEntity Encode(ManagementApplicationEntity x) => new(Partition(x.OrganizationId), Row(x.ApplicationId))
    {
        ["OrganizationId"] = x.OrganizationId, ["ApplicationId"] = x.ApplicationId,
        ["ApplicationKey"] = x.ApplicationKey, ["Name"] = x.Name, ["Environment"] = x.Environment,
        ["BaseUrl"] = x.BaseUrl, ["IsEnabled"] = x.IsEnabled, ["CreatedUtc"] = x.CreatedUtc,
        ["UpdatedUtc"] = x.UpdatedUtc, ["CreatedBy"] = x.CreatedBy, ["UpdatedBy"] = x.UpdatedBy,
        ["ActiveCredentialId"] = x.ActiveCredentialId
    };
    private static ManagementApplicationEntity Decode(TableEntity x) => new()
    {
        OrganizationId = x.GetGuid("OrganizationId")!.Value, ApplicationId = x.GetGuid("ApplicationId")!.Value,
        ApplicationKey = x.GetString("ApplicationKey"), Name = x.GetString("Name"), Environment = x.GetString("Environment"),
        BaseUrl = x.GetString("BaseUrl"), IsEnabled = x.GetBoolean("IsEnabled")!.Value,
        CreatedUtc = x.GetDateTimeOffset("CreatedUtc")!.Value, UpdatedUtc = x.GetDateTimeOffset("UpdatedUtc")!.Value,
        CreatedBy = x.GetString("CreatedBy"), UpdatedBy = x.GetString("UpdatedBy"), ActiveCredentialId = x.GetGuid("ActiveCredentialId"),
        Version = x.ETag.ToString()
    };
}
