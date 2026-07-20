using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.AccessControl;
using Microsoft.Extensions.Options;

namespace IBeam.AccessControl.Repositories.AzureTable;

public sealed class AzureTableResourceAccessStore : IResourceAccessStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableAccessControlOptions _options;

    public AzureTableResourceAccessStore(
        TableServiceClient serviceClient,
        IOptions<AzureTableAccessControlOptions> options)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<IReadOnlyList<ResourceAccessGrantRecord>> ListGrantsAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        var partitionKey = _options.ResourceAccessGrantsPk(tenantId);
        var results = new List<ResourceAccessGrantRecord>();

        await foreach (var entity in table.QueryAsync<ResourceAccessGrantEntity>(
                           x => x.PartitionKey == partitionKey,
                           cancellationToken: ct).ConfigureAwait(false))
        {
            results.Add(ToRecord(entity));
        }

        return results
            .OrderBy(x => x.ResourceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Subject.SubjectType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Subject.SubjectId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ResourceAccessGrantRecord?> GetGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateGrantId(grantId);
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        var response = await table.GetEntityIfExistsAsync<ResourceAccessGrantEntity>(
            _options.ResourceAccessGrantsPk(tenantId),
            _options.ResourceAccessGrantsRk(grantId),
            cancellationToken: ct).ConfigureAwait(false);

        return response.HasValue ? ToRecord(response.Value) : null;
    }

    public async Task<ResourceAccessGrantRecord> UpsertGrantAsync(ResourceAccessGrantRecord grant, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ValidateTenantId(grant.TenantId);
        ValidateGrantId(grant.GrantId);
        var table = await GetTableAsync(ct).ConfigureAwait(false);
        await table.UpsertEntityAsync(ToEntity(grant), TableUpdateMode.Replace, ct).ConfigureAwait(false);
        return grant;
    }

    public async Task DeleteGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateGrantId(grantId);
        var table = await GetTableAsync(ct).ConfigureAwait(false);

        try
        {
            await table.DeleteEntityAsync(
                _options.ResourceAccessGrantsPk(tenantId),
                _options.ResourceAccessGrantsRk(grantId),
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    private async Task<TableClient> GetTableAsync(CancellationToken ct)
    {
        var table = _serviceClient.GetTableClient(_options.FullTableName(_options.ResourceAccessGrantsTableName));
        if (_options.CreateTablesIfNotExists)
        {
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
        }

        return table;
    }

    private ResourceAccessGrantEntity ToEntity(ResourceAccessGrantRecord grant)
        => new()
        {
            PartitionKey = _options.ResourceAccessGrantsPk(grant.TenantId),
            RowKey = _options.ResourceAccessGrantsRk(grant.GrantId),
            GrantId = grant.GrantId,
            TenantId = grant.TenantId,
            ResourceType = grant.ResourceType,
            ResourceId = grant.ResourceId,
            SubjectType = grant.Subject.SubjectType,
            SubjectId = grant.Subject.SubjectId,
            AccessLevel = grant.AccessLevel,
            Status = grant.Status,
            CreatedUtc = grant.CreatedUtc,
            CreatedByUserId = grant.CreatedByUserId,
            UpdatedUtc = grant.UpdatedUtc,
            ExpiresUtc = grant.ExpiresUtc,
            MetadataJson = JsonSerializer.Serialize(grant.Metadata)
        };

    private static ResourceAccessGrantRecord ToRecord(ResourceAccessGrantEntity entity)
        => new(
            GrantId: entity.GrantId,
            TenantId: entity.TenantId,
            ResourceType: entity.ResourceType,
            ResourceId: entity.ResourceId,
            Subject: new AccessSubject(entity.SubjectType, entity.SubjectId),
            AccessLevel: entity.AccessLevel,
            Status: string.IsNullOrWhiteSpace(entity.Status) ? ResourceAccessGrantStatuses.Active : entity.Status,
            CreatedUtc: entity.CreatedUtc,
            CreatedByUserId: entity.CreatedByUserId,
            UpdatedUtc: entity.UpdatedUtc,
            ExpiresUtc: entity.ExpiresUtc,
            Metadata: DeserializeMetadata(entity.MetadataJson));

    private static IReadOnlyDictionary<string, string> DeserializeMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(value)
                       ?.Where(x => !string.IsNullOrWhiteSpace(x.Key))
                       .ToDictionary(x => x.Key, x => x.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new AccessControlException("tenantId is required.");
    }

    private static void ValidateGrantId(Guid grantId)
    {
        if (grantId == Guid.Empty)
            throw new AccessControlException("grantId is required.");
    }
}
