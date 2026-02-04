using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.DataModels.System;
using IBeam.Repositories.AzureTables.Internal;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories.AzureTables;

public sealed class AzureTablesRepositoryStore<T> : IRepositoryStore<T>
    where T : class, IEntity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TableServiceClient _serviceClient;
    private readonly AzureTablesOptions _options;
    private readonly ITenantContext _tenantContext;
    private TableClient? _table;

    private bool IsTenantSpecific => typeof(ITenantEntity).IsAssignableFrom(typeof(T));

    public AzureTablesRepositoryStore(IOptions<AzureTablesOptions> options, ITenantContext tenantContext)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("AzureTablesOptions.ConnectionString must be configured.");

        _serviceClient = new TableServiceClient(_options.ConnectionString);
    }

    private async Task<TableClient> GetTableAsync()
    {
        if (_table != null) return _table;

        var tableName = BuildTableName(typeof(T).Name, _options.TableNamePrefix);
        var table = _serviceClient.GetTableClient(tableName);

        if (_options.CreateTablesIfNotExists)
            await table.CreateIfNotExistsAsync();

        _table = table;
        return table;
    }

    private static string BuildTableName(string typeName, string? prefix)
    {
        var raw = (prefix ?? string.Empty) + typeName;
        var alnum = new string(raw.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(alnum)) alnum = "IBeam";
        if (!char.IsLetter(alnum[0])) alnum = "T" + alnum;
        if (alnum.Length < 3) alnum = alnum.PadRight(3, '0');
        if (alnum.Length > 63) alnum = alnum[..63];
        return alnum;
    }

    private string GetPartitionKeyForRead()
    {
        if (!IsTenantSpecific) return "global";
        var tenantId = _tenantContext.TenantId ?? Guid.Empty;
        return tenantId == Guid.Empty ? "global" : tenantId.ToString("N");
    }

    private static string ToRowKey(Guid id) => id.ToString("N");

    private static AzureTableEnvelope Wrap(T entity, string pk) => new()
    {
        PartitionKey = pk,
        RowKey = ToRowKey(entity.Id),
        Type = typeof(T).FullName,
        Data = JsonSerializer.Serialize(entity, JsonOptions)
    };

    private static T? Unwrap(AzureTableEnvelope? env)
        => env == null || string.IsNullOrWhiteSpace(env.Data)
            ? null
            : JsonSerializer.Deserialize<T>(env.Data, JsonOptions);

    public async Task<T?> GetByIdAsync(Guid id)
    {
        if (id == Guid.Empty) return null;

        var table = await GetTableAsync();
        var pk = GetPartitionKeyForRead();
        var rk = ToRowKey(id);

        var response = await table.GetEntityIfExistsAsync<AzureTableEnvelope>(pk, rk);
        return response.HasValue ? Unwrap(response.Value) : null;
    }

    public async Task<IReadOnlyList<T>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        var idList = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new();
        if (idList.Count == 0) return Array.Empty<T>();

        var tasks = idList.Select(GetByIdAsync).ToList();
        var results = await Task.WhenAll(tasks);
        return results.Where(x => x != null).Cast<T>().ToList();
    }

    public async Task<IReadOnlyList<T>> GetAllAsync()
    {
        var table = await GetTableAsync();
        var pk = GetPartitionKeyForRead();
        var filter = TableClient.CreateQueryFilter<AzureTableEnvelope>(x => x.PartitionKey == pk);

        var list = new List<T>();
        await foreach (var env in table.QueryAsync<AzureTableEnvelope>(filter: filter))
        {
            var entity = Unwrap(env);
            if (entity != null) list.Add(entity);
        }

        return list;
    }

    public async Task<T> UpsertAsync(T entity)
    {
        var table = await GetTableAsync();

        var pk = "global";
        if (entity is ITenantEntity te && te.TenantId != Guid.Empty)
            pk = te.TenantId.ToString("N");
        else if (IsTenantSpecific && _tenantContext.TenantId is Guid tid && tid != Guid.Empty)
            pk = tid.ToString("N");

        await table.UpsertEntityAsync(Wrap(entity, pk), TableUpdateMode.Replace);
        return entity;
    }

    public async Task<IReadOnlyList<T>> UpsertAllAsync(IEnumerable<T> entities)
    {
        var table = await GetTableAsync();
        var list = entities?.Where(x => x != null).ToList() ?? new();
        if (list.Count == 0) return Array.Empty<T>();

        // Must batch by PartitionKey; max 100 per transaction
        var groups = list.GroupBy(e =>
        {
            if (e is ITenantEntity te && te.TenantId != Guid.Empty) return te.TenantId.ToString("N");
            if (IsTenantSpecific && _tenantContext.TenantId is Guid tid && tid != Guid.Empty) return tid.ToString("N");
            return "global";
        });

        foreach (var g in groups)
        {
            var batch = new List<TableTransactionAction>(100);

            foreach (var e in g)
            {
                batch.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, Wrap(e, g.Key)));

                if (batch.Count == 100)
                {
                    await table.SubmitTransactionAsync(batch);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                await table.SubmitTransactionAsync(batch);
        }

        return list;
    }

    public async Task HardDeleteAsync(Guid id)
    {
        if (id == Guid.Empty) return;

        var table = await GetTableAsync();
        var pk = GetPartitionKeyForRead();
        var rk = ToRowKey(id);

        try { await table.DeleteEntityAsync(pk, rk, ETag.All); }
        catch (RequestFailedException ex) when (ex.Status == 404) { /* ignore */ }
    }
}
